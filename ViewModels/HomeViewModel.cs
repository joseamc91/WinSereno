using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinSereno.Infrastructure;
using WinSereno.Models;
using WinSereno.Services;

namespace WinSereno.ViewModels
{
    public sealed class InformationCardViewModel : ObservableObject
    {
        public InformationBlock Block { get; }
        public string Title { get; }
        private string value = "Consultando...";
        public string Value { get => value; set => Set(ref this.value, value); }
        private string description = "";
        public string Description { get => description; set => Set(ref description, value); }
        public InformationCardViewModel(InformationBlock block, string title) { Block = block; Title = title; }
    }
    public sealed class DiskViewModel
    {
        public string Name { get; set; }
        public string CapacityText { get; set; }
        public string FreeText { get; set; }
        public bool NeedsAttention { get; set; }
        public string StatusText => NeedsAttention ? "Atención" : "Correcto";
    }
    public sealed class HomeViewModel : ObservableObject
    {
        private readonly ISystemInformationService service;
        private readonly ISessionLogger logger;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        public ObservableCollection<InformationCardViewModel> Cards { get; } = new ObservableCollection<InformationCardViewModel>();
        public ObservableCollection<DiskViewModel> Disks { get; } = new ObservableCollection<DiskViewModel>();
        public RelayCommand RefreshCommand { get; }
        private bool isRefreshing;
        public bool IsRefreshing { get => isRefreshing; private set { if (Set(ref isRefreshing, value)) RefreshCommand.Refresh(); } }
        private string disksMessage = "Consultando...";
        public string DisksMessage { get => disksMessage; private set => Set(ref disksMessage, value); }
        public bool HasDisksMessage => !string.IsNullOrEmpty(DisksMessage);
        private string refreshedText;
        public string RefreshedText { get => refreshedText; private set => Set(ref refreshedText, value); }
        private bool stopped;
        public HomeViewModel(ISystemInformationService service, ISessionLogger logger)
        {
            this.service = service; this.logger = logger;
            foreach (var entry in new[] { Tuple.Create(InformationBlock.Windows, "Windows"), Tuple.Create(InformationBlock.Cpu, "CPU"), Tuple.Create(InformationBlock.Memory, "RAM instalada"),
                Tuple.Create(InformationBlock.Network, "Red"), Tuple.Create(InformationBlock.Uptime, "Uptime"), Tuple.Create(InformationBlock.Restart, "Reinicio pendiente") })
                Cards.Add(new InformationCardViewModel(entry.Item1, entry.Item2));
            RefreshCommand = new RelayCommand(async p => await RefreshAsync(), p => !IsRefreshing && !stopped);
        }
        public void Stop() { stopped = true; lifetime.Cancel(); }
        public async Task RefreshAsync()
        {
            if (IsRefreshing || stopped) return;
            IsRefreshing = true;
            foreach (var card in Cards) { card.Value = "Consultando..."; card.Description = ""; }
            Disks.Clear(); DisksMessage = "Consultando..."; Raise(nameof(HasDisksMessage)); RefreshedText = "Consultando información del equipo...";
            try
            {
                await service.CollectAsync(new Progress<InformationUpdate>(ApplyUpdate), lifetime.Token);
                if (!stopped) RefreshedText = "Última consulta: " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            }
            catch (Exception ex)
            {
                SystemQuery.Log(logger, "Error de recopilación: " + ex);
                if (!stopped)
                {
                    foreach (var card in Cards.Where(c => c.Value == "Consultando...")) card.Value = "No se pudo consultar";
                    if (DisksMessage == "Consultando...") DisksMessage = "No se pudo consultar";
                    Raise(nameof(HasDisksMessage)); RefreshedText = "Consulta incompleta";
                }
            }
            finally { IsRefreshing = false; }
        }
        private void ApplyUpdate(InformationUpdate update)
        {
            if (stopped) return;
            if (update.Block == InformationBlock.Disks)
            {
                var collection = update.Data as DiskCollection;
                Disks.Clear();
                if (collection != null)
                    foreach (var disk in collection.Volumes)
                        Disks.Add(new DiskViewModel { Name = disk.Unit + (string.IsNullOrWhiteSpace(disk.Label) ? "" : " · " + disk.Label),
                            CapacityText = FormatBytes((ulong)disk.FreeBytes) + " libres de " + FormatBytes((ulong)disk.TotalBytes),
                            FreeText = disk.FreePercentage.ToString("0.#", CultureInfo.CurrentCulture) + " % libre", NeedsAttention = disk.NeedsAttention });
                DisksMessage = collection == null ? "No se pudo consultar" : collection.HasErrors ? "Algunas unidades no se pudieron consultar" : Disks.Count == 0 ? "No hay volúmenes locales listos" : "";
                Raise(nameof(HasDisksMessage)); return;
            }
            var card = Cards.First(c => c.Block == update.Block);
            card.Description = "";
            if (update.Status != InformationStatus.Available) { card.Value = update.Status == InformationStatus.Failed ? "No se pudo consultar" : "No disponible"; return; }
            switch (update.Block)
            {
                case InformationBlock.Windows:
                    var windows = (WindowsInformation)update.Data;
                    card.Value = windows.ProductName ?? "Nombre no disponible";
                    card.Description = Join(windows.DisplayVersion, string.IsNullOrEmpty(windows.Build) ? null : "Build " + windows.Build + (windows.Revision.HasValue ? "." + windows.Revision : ""), windows.Architecture);
                    break;
                case InformationBlock.Cpu:
                    var cpu = (CpuInformation)update.Data;
                    card.Value = string.IsNullOrWhiteSpace(cpu.Model) ? "Modelo no disponible" : cpu.Model;
                    card.Description = Join(cpu.PhysicalCores.HasValue ? cpu.PhysicalCores + " núcleos" : null, cpu.LogicalProcessors.HasValue ? cpu.LogicalProcessors + " hilos" : null);
                    break;
                case InformationBlock.Memory:
                    card.Value = FormatBytes(((MemoryInformation)update.Data).InstalledBytes); card.Description = "Memoria física instalada"; break;
                case InformationBlock.Uptime:
                    var uptime = ((UptimeInformation)update.Data).Uptime;
                    card.Value = uptime.TotalDays >= 1 ? uptime.Days + (uptime.Days == 1 ? " día " : " días ") + uptime.Hours + " h" : (int)uptime.TotalHours + " h " + uptime.Minutes + " min";
                    card.Description = "Desde el último arranque"; break;
                case InformationBlock.Restart:
                    var status = ((RestartPendingInformation)update.Data).Status;
                    card.Value = status == RestartPendingStatus.Pending ? "Sí" : status == RestartPendingStatus.Possible ? "Posible" : status == RestartPendingStatus.NotPending ? "No" : "No se pudo comprobar";
                    card.Description = status == RestartPendingStatus.Pending ? "Windows tiene un reinicio pendiente." : status == RestartPendingStatus.Possible ? "Hay operaciones de archivos pendientes." : "Según indicadores conocidos de Windows; sin acciones automáticas"; break;
                case InformationBlock.Network:
                    var network = (NetworkInformation)update.Data;
                    card.Value = network.NeutralMessage ?? network.Kind;
                    card.Description = network.NeutralMessage != null ? "Sin pruebas de conectividad" : string.Join("\n", new[] { network.Description ?? network.Name,
                        network.SpeedBitsPerSecond.HasValue ? FormatSpeed(network.SpeedBitsPerSecond.Value) : null, network.IPv4 ?? "IPv4 principal no determinada" }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    break;
            }
        }
        private static string Join(params string[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        private static string FormatSpeed(long speed) => speed >= 1000000000 ? (speed / 1000000000.0).ToString("0.##", CultureInfo.CurrentCulture) + " Gbps" : (speed / 1000000.0).ToString("0.##", CultureInfo.CurrentCulture) + " Mbps";
        private static string FormatBytes(ulong bytes)
        {
            var units = new[] { "B", "KB", "MB", "GB", "TB" }; double size = bytes; int unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            return size.ToString("0.#", CultureInfo.CurrentCulture) + " " + units[unit];
        }
    }
}
