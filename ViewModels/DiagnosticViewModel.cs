using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
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
        public event EventHandler<TaskProgress> Completed;
        // Presentation-only snapshot for the global toast; execution remains in the existing service/lease.
        public TaskProgress LiveProgress { get; private set; }
        private bool isRunning;
        public bool IsRunning { get => isRunning; private set => Set(ref isRunning, value); }
        private bool isIntegrityRunning;
        public bool IsIntegrityRunning { get => isIntegrityRunning; private set { if (Set(ref isIntegrityRunning, value)) CancelCommand.Refresh(); } }
        private string integrityPhase;
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
            CancelCommand = new RelayCommand(p => { operations.RequestCancellation(); Summary = "Cancelando diagnóstico... La comprobación actual puede tardar en terminar."; }, p => IsRunning && !IsIntegrityRunning && operations.CanBeCancelled);
            DetailsCommand = new RelayCommand(p => { if (p is DiagnosticResult result) dialogs.ShowDiagnosticDetails(result); });
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, e) => { if (!operations.IsActive) return; if (!Summary.StartsWith("Cancelando", StringComparison.Ordinal)) Summary = (IsIntegrityRunning ? integrityPhase : "Analizando...") + " · " + watch.Elapsed.ToString(@"mm\:ss"); };
            operations.Changed += (s, e) => { AnalyzeCommand.Refresh(); CancelCommand.Refresh(); };
        }
        public async Task AnalyzeAsync()
        {
            if (operations.IsActive || IsRunning) return;
            using (var operation = operations.Begin("Diagnóstico", true))
            {
                var outcome = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now, ExecutionStatus = ExecutionStatus.Success };
                IsRunning = true; CancelCommand.Refresh();
                Results.Clear(); foreach (var item in DiagnosticService.CreatePendingResults()) Results.Add(item);
                CountsChanged(); watch.Restart(); Summary = "Analizando... · 00:00"; timer.Start();
                LiveProgress = null; Raise(nameof(LiveProgress));
                try
                {
                    await service.RunAsync(new Progress<DiagnosticResult>(ApplyResult), operation, new Progress<TaskProgress>(ApplyPhase));
                    operation.Token.ThrowIfCancellationRequested();
                    Summary = "Diagnóstico finalizado en " + watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture) + " s";
                }
                catch (OperationCanceledException) { outcome.ExecutionStatus = ExecutionStatus.Cancelled; Summary = "Diagnóstico cancelado"; }
                catch (Exception ex) { outcome.ExecutionStatus = ExecutionStatus.Failed; Summary = "No se pudo completar el diagnóstico. Los resultados obtenidos se conservan."; SystemQuery.Log(logger, "Error orquestación diagnóstico: " + ex); }
                finally
                {
                    watch.Stop(); timer.Stop(); IsRunning = false; IsIntegrityRunning = false; hasRun = true; Raise(nameof(AnalyzeLabel)); CancelCommand.Refresh();
                    CountsChanged();
                    SystemQuery.Log(logger, "Resumen diagnóstico | Correctos=" + HealthyCount + " | Atención=" + AttentionCount + " | Errores=" + ErrorCount + " | No comprobado=" + NotCheckedCount + " | " + Summary);
                    outcome.FinishedAt = DateTimeOffset.Now; outcome.Duration = watch.Elapsed;
                    outcome.UserSummary = Summary + " · " + HealthyCount + " Correctos · " + AttentionCount + " Atención · " + ErrorCount + " Errores · " + NotCheckedCount + " No comprobado";
                    outcome.FindingStatus = outcome.ExecutionStatus != ExecutionStatus.Success ? FindingStatus.Unknown :
                        ErrorCount > 0 ? FindingStatus.Failed : AttentionCount > 0 ? FindingStatus.Attention : NotCheckedCount > 0 ? FindingStatus.PartiallyCompleted : FindingStatus.Healthy;
                    var text = new StringBuilder();
                    foreach (var result in Results)
                    {
                        text.AppendLine(result.Name + " · " + result.StatusLabel);
                        text.AppendLine(result.Summary); text.AppendLine(result.DetailedDescription);
                        text.AppendLine(result.Recommendation); text.AppendLine(result.TechnicalDetails);
                        foreach (var item in result.Events) text.AppendLine(item.Time + " · " + item.Provider + " · " + item.EventId + " · " + item.Level + "\n" + item.Interpretation + "\n" + item.WindowsDescription);
                        text.AppendLine();
                    }
                    outcome.StdOut = text.ToString();
                    Completed?.Invoke(this, new TaskProgress { State = RunnerState.Completed,
                        CurrentTask = new MaintenanceTask { Id = "diagnosis.general", Name = "Diagnóstico" }, Result = outcome,
                        StartedAt = outcome.StartedAt, Elapsed = outcome.Duration, StdOut = outcome.StdOut, LastRelevantLine = outcome.UserSummary });
                }
            }
        }
        private void ApplyResult(DiagnosticResult result)
        {
            for (int i = 0; i < Results.Count; i++) if (Results[i].Id == result.Id) { Results[i] = result; break; }
            CountsChanged();
        }
        private void ApplyPhase(TaskProgress progress)
        {
            if (!IsRunning || progress.State != RunnerState.Running) return;
            LiveProgress = progress; Raise(nameof(LiveProgress));
            IsIntegrityRunning = progress.CurrentTask?.Id == ElevatedTaskCatalog.DiagnosticIntegrityId;
            integrityPhase = progress.StepLabel == null ? "Solicitando permisos de administrador..." : "Comprobando integridad de Windows...";
            if (progress.Percentage.HasValue) integrityPhase += " · " + progress.Percentage.Value.ToString("0.#") + " %";
            Summary = (IsIntegrityRunning ? integrityPhase : "Analizando...") + " · " + watch.Elapsed.ToString(@"mm\:ss");
        }
        private void CountsChanged() { Raise(nameof(HealthyCount)); Raise(nameof(AttentionCount)); Raise(nameof(ErrorCount)); Raise(nameof(NotCheckedCount)); }
    }
}
