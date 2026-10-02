using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using WinSereno.Infrastructure;
using WinSereno.Models;
using WinSereno.Services;

namespace WinSereno.ViewModels
{
    public sealed class DiagnosticViewModel : ObservableObject
    {
        private readonly DiagnosticService service;
        private readonly OperationCoordinator operations;
        private readonly ISessionLogger logger;
        private readonly DispatcherTimer timer;
        private readonly Stopwatch watch = new Stopwatch();
        public ObservableCollection<DiagnosticResult> Results { get; } = new ObservableCollection<DiagnosticResult>();
        public RelayCommand AnalyzeCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand DetailsCommand { get; }
        private bool isRunning;
        public bool IsRunning { get => isRunning; private set => Set(ref isRunning, value); }
        private bool hasRun;
        public string AnalyzeLabel => hasRun ? "Analizar de nuevo" : "Analizar este PC";
        private string summary = "Aún no se ha analizado este PC en esta sesión.";
        public string Summary { get => summary; private set => Set(ref summary, value); }
        public int HealthyCount => Results.Count(r => r.Status == DiagnosticStatus.Healthy);
        public int AttentionCount => Results.Count(r => r.Status == DiagnosticStatus.Attention);
        public int ErrorCount => Results.Count(r => r.Status == DiagnosticStatus.Error);
        public int NotCheckedCount => Results.Count(r => r.Status == DiagnosticStatus.NotChecked);
        public DiagnosticViewModel(DiagnosticService service, OperationCoordinator operations, ISessionLogger logger, IDialogService dialogs, IntegritySessionState integrity)
        {
            this.service = service; this.operations = operations; this.logger = logger;
            foreach (var result in DiagnosticService.CreatePendingResults()) Results.Add(result);
            AnalyzeCommand = new RelayCommand(async p => await AnalyzeAsync(), p => !operations.IsActive && !IsRunning);
            CancelCommand = new RelayCommand(p => { operations.RequestCancellation(); Summary = "Cancelando diagnóstico... La comprobación actual puede tardar en terminar."; }, p => IsRunning);
            DetailsCommand = new RelayCommand(p => { if (p is DiagnosticResult result) dialogs.ShowDiagnosticDetails(result); });
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, e) => { if (!operations.IsActive) return; if (!Summary.StartsWith("Cancelando", StringComparison.Ordinal)) Summary = "Analizando... · " + watch.Elapsed.ToString(@"mm\:ss"); };
            operations.Changed += (s, e) => { AnalyzeCommand.Refresh(); CancelCommand.Refresh(); };
            integrity.Changed += (s, e) => ApplyResult(integrity.Read());
        }
        public async Task AnalyzeAsync()
        {
            if (operations.IsActive || IsRunning) return;
            using (var operation = operations.Begin("Diagnóstico", true))
            {
                IsRunning = true; CancelCommand.Refresh();
                Results.Clear(); foreach (var item in DiagnosticService.CreatePendingResults()) Results.Add(item);
                CountsChanged(); watch.Restart(); Summary = "Analizando... · 00:00"; timer.Start();
                try
                {
                    await service.RunAsync(new Progress<DiagnosticResult>(ApplyResult), operation.Token);
                    operation.Token.ThrowIfCancellationRequested();
                    Summary = "Diagnóstico finalizado en " + watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture) + " s";
                }
                catch (OperationCanceledException) { Summary = "Diagnóstico cancelado"; }
                catch (Exception ex) { Summary = "No se pudo completar el diagnóstico. Los resultados obtenidos se conservan."; SystemQuery.Log(logger, "Error orquestación diagnóstico: " + ex); }
                finally
                {
                    watch.Stop(); timer.Stop(); IsRunning = false; hasRun = true; Raise(nameof(AnalyzeLabel)); CancelCommand.Refresh();
                    CountsChanged();
                    SystemQuery.Log(logger, "Resumen diagnóstico | Correctos=" + HealthyCount + " | Atención=" + AttentionCount + " | Errores=" + ErrorCount + " | No comprobado=" + NotCheckedCount + " | " + Summary);
                }
            }
        }
        private void ApplyResult(DiagnosticResult result)
        {
            for (int i = 0; i < Results.Count; i++) if (Results[i].Id == result.Id) { Results[i] = result; break; }
            CountsChanged();
        }
        private void CountsChanged() { Raise(nameof(HealthyCount)); Raise(nameof(AttentionCount)); Raise(nameof(ErrorCount)); Raise(nameof(NotCheckedCount)); }
    }
}
