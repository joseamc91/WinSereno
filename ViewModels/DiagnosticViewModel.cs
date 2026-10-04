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
        public string AnalyzeLabel => hasRun ? WinSereno.Localization.LocalizationService.Source("Text.AnalyzeAgain") : WinSereno.Localization.LocalizationService.Source("Text.AnalyzeThisPc");
        private string summary = WinSereno.Localization.LocalizationService.Source("Text.ThisPcHasNotBeenAnalyzedInThis");
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
            CancelCommand = new RelayCommand(p => { operations.RequestCancellation(); Summary = WinSereno.Localization.LocalizationService.Source("Text.CancellingDiagnosticsTheCurrentCheckMayTakeSome"); }, p => IsRunning && !IsIntegrityRunning && operations.CanBeCancelled);
            DetailsCommand = new RelayCommand(p => { if (p is DiagnosticResult result) dialogs.ShowDiagnosticDetails(result); });
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, e) => { if (!operations.IsActive) return; if (!Summary.StartsWith(WinSereno.Localization.LocalizationService.Source("Text.CancellingAction"), StringComparison.Ordinal)) Summary = (IsIntegrityRunning ? integrityPhase : WinSereno.Localization.LocalizationService.Source("Text.Analyzing")) + WinSereno.Localization.LocalizationService.Source("Text.Separator") + watch.Elapsed.ToString(@"mm\:ss"); };
            operations.Changed += (s, e) => { AnalyzeCommand.Refresh(); CancelCommand.Refresh(); };
        }
        public async Task AnalyzeAsync()
        {
            if (operations.IsActive || IsRunning) return;
            using (var operation = operations.Begin(WinSereno.Localization.LocalizationService.Source("Text.Diagnostics"), true))
            {
                var outcome = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now, ExecutionStatus = ExecutionStatus.Success };
                IsRunning = true; CancelCommand.Refresh();
                Results.Clear(); foreach (var item in DiagnosticService.CreatePendingResults()) Results.Add(item);
                CountsChanged(); watch.Restart(); Summary = WinSereno.Localization.LocalizationService.Source("Text.Analyzing0000"); timer.Start();
                LiveProgress = null; Raise(nameof(LiveProgress));
                try
                {
                    await service.RunAsync(new Progress<DiagnosticResult>(ApplyResult), operation, new Progress<TaskProgress>(ApplyPhase));
                    operation.Token.ThrowIfCancellationRequested();
                    Summary = WinSereno.Localization.LocalizationService.Source("Text.DiagnosticsCompletedIn") + watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture) + " s";
                }
                catch (OperationCanceledException) { outcome.ExecutionStatus = ExecutionStatus.Cancelled; Summary = WinSereno.Localization.LocalizationService.Source("Text.DiagnosticsCancelled"); }
                catch (Exception ex) { outcome.ExecutionStatus = ExecutionStatus.Failed; Summary = WinSereno.Localization.LocalizationService.Source("Text.DiagnosticsCouldNotBeCompletedResultsObtainedSo"); SystemQuery.Log(logger, "Error orquestación diagnóstico: " + ex); }
                finally
                {
                    watch.Stop(); timer.Stop(); IsRunning = false; IsIntegrityRunning = false; hasRun = true; Raise(nameof(AnalyzeLabel)); CancelCommand.Refresh();
                    CountsChanged();
                    SystemQuery.Log(logger, "Resumen diagnóstico | Correctos=" + HealthyCount + " | Atención=" + AttentionCount + " | Errores=" + ErrorCount + " | No comprobado=" + NotCheckedCount + " | " + Summary);
                    outcome.FinishedAt = DateTimeOffset.Now; outcome.Duration = watch.Elapsed;
                    outcome.UserSummary = Summary + WinSereno.Localization.LocalizationService.Source("Text.Separator") + HealthyCount + " Correctos · " + AttentionCount + WinSereno.Localization.LocalizationService.Source("Text.Attention534") + ErrorCount + " Errores · " + NotCheckedCount + WinSereno.Localization.LocalizationService.Source("Text.NotChecked535");
                    outcome.FindingStatus = outcome.ExecutionStatus != ExecutionStatus.Success ? FindingStatus.Unknown :
                        ErrorCount > 0 ? FindingStatus.Failed : AttentionCount > 0 ? FindingStatus.Attention : NotCheckedCount > 0 ? FindingStatus.PartiallyCompleted : FindingStatus.Healthy;
                    var text = new StringBuilder();
                    foreach (var result in Results)
                    {
                        text.AppendLine(result.Name + WinSereno.Localization.LocalizationService.Source("Text.Separator") + result.StatusLabel);
                        text.AppendLine(result.Summary); text.AppendLine(result.DetailedDescription);
                        text.AppendLine(result.Recommendation); text.AppendLine(result.TechnicalDetails);
                        foreach (var item in result.Events) text.AppendLine(item.Time + WinSereno.Localization.LocalizationService.Source("Text.Separator") + item.Provider + WinSereno.Localization.LocalizationService.Source("Text.Separator") + item.EventId + WinSereno.Localization.LocalizationService.Source("Text.Separator") + item.Level + "\n" + item.Interpretation + "\n" + item.WindowsDescription);
                        text.AppendLine();
                    }
                    outcome.StdOut = text.ToString();
                    Completed?.Invoke(this, new TaskProgress { State = RunnerState.Completed,
                        CurrentTask = new MaintenanceTask { Id = "diagnosis.general", Name = WinSereno.Localization.LocalizationService.Source("Text.Diagnostics") }, Result = outcome,
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
            integrityPhase = progress.StepLabel == null ? WinSereno.Localization.LocalizationService.Source("Text.RequestingAdministratorPrivileges") : WinSereno.Localization.LocalizationService.Source("Text.CheckingWindowsIntegrity");
            if (progress.Percentage.HasValue) integrityPhase += WinSereno.Localization.LocalizationService.Source("Text.Separator") + progress.Percentage.Value.ToString("0.#") + " %";
            Summary = (IsIntegrityRunning ? integrityPhase : WinSereno.Localization.LocalizationService.Source("Text.Analyzing")) + WinSereno.Localization.LocalizationService.Source("Text.Separator") + watch.Elapsed.ToString(@"mm\:ss");
        }
        private void CountsChanged() { Raise(nameof(HealthyCount)); Raise(nameof(AttentionCount)); Raise(nameof(ErrorCount)); Raise(nameof(NotCheckedCount)); }
    }
}
