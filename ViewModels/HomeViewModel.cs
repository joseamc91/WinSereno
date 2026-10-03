using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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
        private string title;
        public string Title { get => title; set => Set(ref title, value); }
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
        private CpuInformation currentCpu;
        private GpuInformation currentGpu;
        public ObservableCollection<InformationCardViewModel> Cards { get; } = new ObservableCollection<InformationCardViewModel>();
        public ObservableCollection<DiskViewModel> LocalDisks { get; } = new ObservableCollection<DiskViewModel>();
        public ObservableCollection<DiskViewModel> ExternalDisks { get; } = new ObservableCollection<DiskViewModel>();
        public bool HasExternalDisks => ExternalDisks.Count > 0;
        public RelayCommand RefreshCommand { get; }
        private bool isRefreshing;
        public bool IsRefreshing { get => isRefreshing; private set { if (Set(ref isRefreshing, value)) RefreshCommand.Refresh(); } }
        private string disksMessage = "Consultando...";
        public string DisksMessage { get => disksMessage; private set => Set(ref disksMessage, value); }
        public bool HasDisksMessage => !string.IsNullOrEmpty(DisksMessage);
        private string externalDisksMessage = "";
        public string ExternalDisksMessage { get => externalDisksMessage; private set => Set(ref externalDisksMessage, value); }
        public bool HasExternalDisksMessage => !string.IsNullOrEmpty(ExternalDisksMessage);
        private string refreshedText;
        public string RefreshedText { get => refreshedText; private set => Set(ref refreshedText, value); }
        private bool stopped;
        public HomeViewModel(ISystemInformationService service, ISessionLogger logger)
        {
            this.service = service; this.logger = logger;
            foreach (var entry in new[] { Tuple.Create(InformationBlock.Windows, "Windows"), Tuple.Create(InformationBlock.Cpu, "CPU"), Tuple.Create(InformationBlock.Gpu, "GPU"),
                Tuple.Create(InformationBlock.Memory, "RAM instalada"), Tuple.Create(InformationBlock.Network, "Red"), Tuple.Create(InformationBlock.Uptime, "Uptime") })
                Cards.Add(new InformationCardViewModel(entry.Item1, entry.Item2));
            RefreshCommand = new RelayCommand(async p => await RefreshAsync(), p => !IsRefreshing && !stopped);
        }
        public void Stop() { stopped = true; lifetime.Cancel(); }
        public async Task RefreshAsync()
        {
            if (IsRefreshing || stopped) return;
            IsRefreshing = true;
            currentCpu = null; currentGpu = null;
            Cards.First(c => c.Block == InformationBlock.Gpu).Title = "GPU";
            foreach (var card in Cards) { card.Value = "Consultando..."; card.Description = ""; }
            LocalDisks.Clear(); ExternalDisks.Clear(); ExternalDisksMessage = "";
            Raise(nameof(HasExternalDisks)); Raise(nameof(HasExternalDisksMessage));
            DisksMessage = "Consultando..."; Raise(nameof(HasDisksMessage)); RefreshedText = "Consultando información del equipo...";
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
                LocalDisks.Clear(); ExternalDisks.Clear();
                if (collection != null)
                    foreach (var disk in collection.Volumes.OrderBy(d => d.Unit, StringComparer.OrdinalIgnoreCase))
                    {
                        if (disk.DriveType != System.IO.DriveType.Fixed && disk.DriveType != System.IO.DriveType.Removable) continue;
                        var target = disk.DriveType == System.IO.DriveType.Removable ? ExternalDisks : LocalDisks;
                        target.Add(new DiskViewModel { Name = disk.Unit + (string.IsNullOrWhiteSpace(disk.Label) ? "" : " · " + disk.Label),
                            CapacityText = FormatBytes((ulong)disk.FreeBytes) + " libres de " + FormatBytes((ulong)disk.TotalBytes),
                            FreeText = disk.FreePercentage.ToString("0.#", CultureInfo.CurrentCulture) + " % libre", NeedsAttention = disk.NeedsAttention });
                    }
                DisksMessage = collection == null ? "No se pudo consultar" : collection.HasErrors ? "Algunas unidades no se pudieron consultar" : LocalDisks.Count == 0 ? "No hay volúmenes locales listos" : "";
                ExternalDisksMessage = HasExternalDisks && collection.HasErrors ? "Algunas unidades no se pudieron consultar" : "";
                Raise(nameof(HasDisksMessage)); Raise(nameof(HasExternalDisks)); Raise(nameof(HasExternalDisksMessage)); return;
            }
            var card = Cards.First(c => c.Block == update.Block);
            card.Description = "";
            if (update.Status != InformationStatus.Available)
            {
                if (update.Block == InformationBlock.Cpu) currentCpu = null;
                if (update.Block == InformationBlock.Gpu) { currentGpu = null; card.Title = "GPU"; UpdateCpuPresentation(); }
                card.Value = update.Status == InformationStatus.Failed ? "No se pudo consultar" : "No disponible"; return;
            }
            switch (update.Block)
            {
                case InformationBlock.Windows:
                    var windows = (WindowsInformation)update.Data;
                    card.Value = windows.ProductName ?? "Nombre no disponible";
                    card.Description = Join(windows.DisplayVersion, string.IsNullOrEmpty(windows.Build) ? null : "Build " + windows.Build + (windows.Revision.HasValue ? "." + windows.Revision : ""), windows.Architecture);
                    break;
                case InformationBlock.Cpu:
                    currentCpu = (CpuInformation)update.Data;
                    UpdateCpuPresentation();
                    break;
                case InformationBlock.Gpu:
                    var gpu = (GpuInformation)update.Data;
                    currentGpu = gpu;
                    card.Title = gpu.IsDedicated ? "GPU dedicada" : "GPU";
                    card.Value = CleanGraphicsName(gpu.Name);
                    card.Description = string.IsNullOrWhiteSpace(gpu.DriverVersion) ? "" : "Controlador " + gpu.DriverVersion;
                    UpdateCpuPresentation();
                    break;
                case InformationBlock.Memory:
                    var memory = (MemoryInformation)update.Data;
                    card.Value = FormatBytes(memory.InstalledBytes); card.Description = FormatMemoryDescription(memory); break;
                case InformationBlock.Uptime:
                    var uptime = ((UptimeInformation)update.Data).Uptime;
                    card.Value = uptime.TotalDays >= 1 ? uptime.Days + (uptime.Days == 1 ? " día " : " días ") + uptime.Hours + " h" : (int)uptime.TotalHours + " h " + uptime.Minutes + " min";
                    card.Description = "Desde el último arranque"; break;
                case InformationBlock.Network:
                    var network = (NetworkInformation)update.Data;
                    card.Value = network.NeutralMessage ?? network.Kind;
                    card.Description = network.NeutralMessage != null ? "Sin pruebas de conectividad" : string.Join("\n", new[] { network.Description ?? network.Name,
                        Join(network.SpeedBitsPerSecond.HasValue ? FormatSpeed(network.SpeedBitsPerSecond.Value) : null,
                            network.IPv4 != null ? "IPv4 " + network.IPv4 : "IPv4 principal no determinada") }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    break;
            }
        }
        private void UpdateCpuPresentation()
        {
            if (currentCpu == null) return;
            var card = Cards.First(c => c.Block == InformationBlock.Cpu);
            string model = CleanCpuName(currentCpu.Model);
            string frequency = currentCpu.MaxClockSpeedMHz.HasValue && SystemInformationPolicy.IsReasonableCpuClockSpeed(currentCpu.MaxClockSpeedMHz.Value)
                ? (currentCpu.MaxClockSpeedMHz.Value / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " GHz" : null;
            card.Value = string.IsNullOrWhiteSpace(model) ? "Modelo no disponible" : model;
            card.Description = string.Join("\n", new[] {
                Join(currentCpu.PhysicalCores.HasValue ? currentCpu.PhysicalCores + " núcleos" : null,
                    currentCpu.LogicalProcessors.HasValue ? currentCpu.LogicalProcessors + " hilos" : null, frequency),
                CleanGraphicsName(currentGpu?.IntegratedName) }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        private static string CleanGraphicsName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;
            string clean = Regex.Replace(name, @"\((?:R|TM)\)", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return Regex.Replace(clean, @"\s+", " ").Trim();
        }
        private static string CleanCpuName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;
            // Names may contain several physical processors joined by the information provider.
            return string.Join(" / ", name.Split(new[] { " / " }, StringSplitOptions.None).Select(part =>
            {
                string clean = CleanGraphicsName(part);
                var options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
                clean = Regex.Replace(clean, @"\s+CPU\s*@\s*\d+(?:[.,]\d+)?\s*(?:GHz|MHz)\s*$", "", options);
                clean = Regex.Replace(clean, @"\s+with\s+Radeon\s+Graphics\s*$", "", options);
                clean = Regex.Replace(clean, @"\s+\d+-Core\s+Processor\s*$", "", options);
                // Limit the standalone generic suffix to identifiable CPU vendors.
                if (Regex.IsMatch(clean, @"^(?:Intel|AMD)\s+\S.+\s+Processor\s*$", options))
                    clean = Regex.Replace(clean, @"\s+Processor\s*$", "", options);
                return Regex.Replace(clean, @"\s+", " ").Trim();
            }).Distinct());
        }
        private static string FormatMemoryDescription(MemoryInformation memory)
        {
            var modules = memory.Modules;
            if (modules == null || modules.Count == 0 || modules.Any(m => m == null)) return "";
            string type = MemoryType(modules[0].SmbiosMemoryType);
            if (modules.Any(m => MemoryType(m.SmbiosMemoryType) != type)) type = null;
            string layout = modules.All(m => m.CapacityBytes > 0 && m.CapacityBytes == modules[0].CapacityBytes)
                ? modules.Count + " × " + FormatBytes(modules[0].CapacityBytes)
                : modules.Count + (modules.Count == 1 ? " módulo" : " módulos");
            uint speed = modules[0].ConfiguredSpeed;
            // SMBIOS Type 17 reports transfer rate. 0/FFFF denote an unknown/extended value.
            string configuredSpeed = speed > 0 && speed < ushort.MaxValue && modules.All(m => m.ConfiguredSpeed == speed)
                ? speed.ToString(CultureInfo.CurrentCulture) + " MT/s" : null;
            return Join(type, layout, configuredSpeed);
        }
        private static string MemoryType(uint value)
        {
            switch (value) { case 24: return "DDR3"; case 26: return "DDR4"; case 34: return "DDR5"; default: return null; }
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
