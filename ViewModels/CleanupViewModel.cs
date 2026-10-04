using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using WinSereno.Infrastructure;
using WinSereno.Models;
using WinSereno.Services;
namespace WinSereno.ViewModels
{
    public sealed class CleanupCategoryViewModel : ObservableObject
    {
        public CleanupCategoryResult Result { get; }
        private bool isSelected;
        public bool IsSelected { get => isSelected; set => Set(ref isSelected, value); }
        public CleanupCategoryViewModel(CleanupCategoryResult result) { Result = result; isSelected = result.Category != CleanupCategory.RecycleBin; }
        public string RecoverableEstimate => WinSereno.Localization.LocalizationService.Source("Text.EstimatedRecoverableSpace") + (!Result.WasAnalyzed || !Result.IsAvailable ? WinSereno.Localization.LocalizationService.Source("Text.Unavailable753") :
            Result.Category == CleanupCategory.RecycleBin ? FormatBytes(Result.TotalBytes) : Result.PotentiallyCleanableBytes.HasValue ? FormatBytes(Result.PotentiallyCleanableBytes.Value) : WinSereno.Localization.LocalizationService.Source("Text.Unavailable753"));
        public bool CanClean => Result.WasAnalyzed && Result.IsAvailable && !Result.WasCancelled && Result.TotalBytes >= 0 &&
            (IsRecycleBin || (!string.IsNullOrWhiteSpace(Result.Path) && Result.PotentiallyCleanableBytes.HasValue &&
                Result.PotentiallyCleanableBytes >= 0 && Result.PotentiallyCleanableBytes <= Result.TotalBytes)) &&
            (!IsWindowsTemporary || Result.AnalysisFinishedAt.HasValue);
        public string Status => !Result.WasAnalyzed ? WinSereno.Localization.LocalizationService.Source("Text.NotAnalyzedYet") : !Result.IsAvailable || Result.WasCancelled ? WinSereno.Localization.LocalizationService.Source("Text.NotChecked") : Result.IsPartial ? WinSereno.Localization.LocalizationService.Source("Text.PartialResult") : WinSereno.Localization.LocalizationService.Source("Text.Analyzed");
        public bool HasElevatedAnalysisTime => IsWindowsTemporary && Result.AnalysisFinishedAt.HasValue;
        public string ElevatedAnalysisText => HasElevatedAnalysisTime ? FormatAnalysisTime(Result.AnalysisFinishedAt.Value) : "";
        internal static string FormatAnalysisTime(DateTimeOffset finished)
            => WinSereno.Localization.LocalizationService.Source("Text.LastElevatedAnalysis") + finished.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
        public bool IsWindowsTemporary => Result.Category == CleanupCategory.WindowsTemporary;
        public bool IsRecycleBin => Result.Category == CleanupCategory.RecycleBin;
        public bool IsThumbnailCache => Result.Category == CleanupCategory.ThumbnailCache;
        public bool IsUserTemporary => Result.Category == CleanupCategory.UserTemporary;
        public string Name => Result.Name;
        public string Path => Result.Path;
        public string DisplayPath => IsRecycleBin ? WinSereno.Localization.LocalizationService.Source("Text.CurrentUserSRecycleBin") : Path;
        public bool HasAmounts => Result.WasAnalyzed && Result.IsAvailable && !Result.WasCancelled;
        public string Summary => !Result.WasAnalyzed ? WinSereno.Localization.LocalizationService.Source("Text.NotAnalyzedYet") : !Result.IsAvailable || Result.WasCancelled ? WinSereno.Localization.LocalizationService.Source("Text.NotChecked") :
            IsRecycleBin ? FormatBytes(Result.TotalBytes) + WinSereno.Localization.LocalizationService.Source("Text.InTheRecycleBin") :
            FormatBytes(Result.TotalBytes) + WinSereno.Localization.LocalizationService.Source("Text.Found711") + (Result.PotentiallyCleanableBytes.HasValue ? WinSereno.Localization.LocalizationService.Source("Text.Separator") + FormatBytes(Result.PotentiallyCleanableBytes.Value) + WinSereno.Localization.LocalizationService.Source("Text.PotentiallyCleanable") : WinSereno.Localization.LocalizationService.Source("Text.EstimateUnavailable"));
        public string Details => !Result.WasAnalyzed ? WinSereno.Localization.LocalizationService.Source("Text.ClickAnalyzeToCheckThisCategory") :
            Result.FileCount.ToString("N0") + (Result.Category == CleanupCategory.RecycleBin ? WinSereno.Localization.LocalizationService.Source("Text.Items") : WinSereno.Localization.LocalizationService.Source("Text.AccessibleFiles")) +
            (Result.IsAvailable && Result.PotentiallyCleanableFileCount.HasValue ? WinSereno.Localization.LocalizationService.Source("Text.Separator") + Result.PotentiallyCleanableFileCount.Value.ToString("N0") + WinSereno.Localization.LocalizationService.Source("Text.PotentiallyCleanable") : "") +
            (Result.IsPartial ? WinSereno.Localization.LocalizationService.Source("Text.PartialResult44") + Result.InaccessibleCount + WinSereno.Localization.LocalizationService.Source("Text.InaccessibleItemsQueries") + Result.AccessDeniedCount + WinSereno.Localization.LocalizationService.Source("Text.PermissionDenials") + Result.LockedCount + WinSereno.Localization.LocalizationService.Source("Text.Locked") + Result.OtherErrorsCount + WinSereno.Localization.LocalizationService.Source("Text.OtherErrors") : "") +
            (Result.ReparsePointCount > 0 ? WinSereno.Localization.LocalizationService.Source("Text.Separator") + Result.ReparsePointCount + WinSereno.Localization.LocalizationService.Source("Text.LinksReparsePointsSkipped") : "") + "\n" + Result.Information;
        public static string FormatBytes(long bytes)
        {
            var units = new[] { "B", "KB", "MB", "GB", "TB" }; double size = bytes; int unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            return size.ToString("0.#", CultureInfo.CurrentCulture) + " " + units[unit];
        }
    }
    public sealed class CleanupViewModel : ObservableObject
    {
        private readonly Func<IProgress<CleanupCategoryResult>, CancellationToken, Task<CleanupAnalysisResult>> analyze;
        private readonly Func<OperationLease, Action<TaskProgress>, Task<MaintenanceTaskResult>> elevatedAnalysis;
        private readonly OperationCoordinator operations;
        private readonly ISessionLogger logger;
        public ObservableCollection<CleanupCategoryViewModel> Categories { get; } = new ObservableCollection<CleanupCategoryViewModel>();
        public RelayCommand AnalyzeCommand { get; }
        public event EventHandler<TaskProgress> Completed;
        public bool CanChangeSelection => !operations.IsActive;
        public CleanupSelection SelectedCategories
        { get { CleanupSelection value = CleanupSelection.None; foreach (var row in Categories) if (row.IsSelected) value |= (CleanupSelection)(1 << (int)row.Result.Category); return value; } }
        public bool HasSelectedCategories => SelectedCategories != CleanupSelection.None;
        public bool CanCleanSelected => HasSelectedCategories && Categories.Where(c => c.IsSelected).All(c => c.CanClean);
        public bool CanClean(CleanupCategory category) => Categories.Any(c => c.Result.Category == category && c.CanClean);
        public string SelectionSummary
        {
            get {
                long bytes = 0; int count = 0, unknown = 0;
                foreach (var row in Categories) if (row.IsSelected) {
                    count++; var value = row.Result; long? estimate = !row.CanClean ? null : value.Category == CleanupCategory.RecycleBin ? value.TotalBytes : value.PotentiallyCleanableBytes;
                    if (estimate.HasValue) bytes += estimate.Value; else unknown++;
                }
                var selected = count + (count == 1 ? WinSereno.Localization.LocalizationService.Source("Text.CategorySelected") : WinSereno.Localization.LocalizationService.Source("Text.CategoriesSelected"));
                if (!hasRun) return selected;
                if (count == 0) return selected;
                if (unknown == count) return selected + WinSereno.Localization.LocalizationService.Source("Text.EstimateUnavailable");
                bool partial = unknown > 0 || Categories.Any(c => c.IsSelected && c.Result.IsPartial);
                return selected + (partial ? WinSereno.Localization.LocalizationService.Source("Text.PartialEstimate") : WinSereno.Localization.LocalizationService.Source("Text.EstimatedRecoverableSpace49")) + CleanupCategoryViewModel.FormatBytes(bytes);
            }
        }
        private void SelectionChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        { if (e.PropertyName == nameof(CleanupCategoryViewModel.IsSelected)) { Raise(nameof(SelectedCategories)); Raise(nameof(HasSelectedCategories)); Raise(nameof(CanCleanSelected)); Raise(nameof(SelectionSummary)); } }
        private bool isRunning;
        public bool IsRunning { get => isRunning; private set => Set(ref isRunning, value); }
        public CleanupCategoryResult ElevatedWindowsTempAnalysis => elevatedWindowsTemp;
        private CleanupCategoryResult elevatedWindowsTemp;
        public void SetWindowsTempAnalysis(CleanupCategoryResult result)
        {
            if (result == null || result.Category != CleanupCategory.WindowsTemporary || !result.WasAnalyzed || !result.IsAvailable ||
                result.WasCancelled || !result.AnalysisFinishedAt.HasValue || result.AnalysisFinishedAt.Value == default(DateTimeOffset)) return;
            if (elevatedWindowsTemp != null && result.AnalysisFinishedAt < elevatedWindowsTemp.AnalysisFinishedAt) return;
            elevatedWindowsTemp = result; Apply(result);
        }
        private bool hasRun;
        public string AnalyzeLabel => hasRun ? WinSereno.Localization.LocalizationService.Source("Text.AnalyzeAgain") : WinSereno.Localization.LocalizationService.Source("Text.Analyze");
        private string summary = WinSereno.Localization.LocalizationService.Source("Text.NoAnalysisHasBeenPerformedYet");
        public string Summary { get => summary; private set => Set(ref summary, value); }
        public CleanupCategoryResult ThumbnailAnalysis
        {
            get { foreach (var category in Categories) if (category.Result.Category == CleanupCategory.ThumbnailCache) return category.Result; return null; }
        }
        public void SetCategoryAnalysis(CleanupCategoryResult result) { Apply(result); }
        public void SetRecycleBinAnalysis(CleanupCategoryResult result) { Apply(result); }
        public void SetThumbnailAnalysis(CleanupCategoryResult result) { Apply(result); }
        public CleanupAnalysisResult LastResult { get; private set; }
        public CleanupViewModel(CleanupAnalysisService service, OperationCoordinator operations, ISessionLogger logger,
            Func<OperationLease, Action<TaskProgress>, Task<MaintenanceTaskResult>> elevatedAnalysis = null)
            : this(service.AnalyzeNormalAsync, operations, logger, elevatedAnalysis) { }
        internal CleanupViewModel(Func<IProgress<CleanupCategoryResult>, CancellationToken, Task<CleanupAnalysisResult>> analyze, OperationCoordinator operations, ISessionLogger logger,
            Func<OperationLease, Action<TaskProgress>, Task<MaintenanceTaskResult>> elevatedAnalysis = null)
        {
            this.analyze = analyze; this.operations = operations; this.logger = logger; this.elevatedAnalysis = elevatedAnalysis;
            foreach (var result in CleanupAnalysisService.CreatePendingResults()) Apply(result);
            AnalyzeCommand = new RelayCommand(async p => await AnalyzeAsync(), p => !operations.IsActive && !IsRunning);
            operations.Changed += (s, e) => { AnalyzeCommand.Refresh(); Raise(nameof(CanChangeSelection)); };
        }
        public async Task AnalyzeAsync(bool recordHistory = true)
        {
            if (operations.IsActive || IsRunning) return;
            using (var operation = operations.Begin(WinSereno.Localization.LocalizationService.Source("Text.CleanupAnalysis"), true))
            {
                var outcome = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now, ExecutionStatus = ExecutionStatus.Success };
                var watch = Stopwatch.StartNew();
                var previousWindows = Categories.Single(c => c.IsWindowsTemporary).Result;
                IsRunning = true; Summary = WinSereno.Localization.LocalizationService.Source("Text.AnalyzingFiles"); LastResult = null;
                foreach (var result in CleanupAnalysisService.CreatePendingResults()) Apply(result);
                try
                {
                    LastResult = await analyze(new Progress<CleanupCategoryResult>(r => { if (r.Category != CleanupCategory.WindowsTemporary) Apply(r); }), operation.Token);
                    // Use the returned snapshot too: progress callbacks may still be queued on the dispatcher.
                    foreach (var result in LastResult.Categories.Where(r => r.Category != CleanupCategory.WindowsTemporary)) Apply(result);
                    if (LastResult.WasCancelled) throw new OperationCanceledException();
                    operation.Token.ThrowIfCancellationRequested();
                    if (recordHistory)
                    {
                        Summary = WinSereno.Localization.LocalizationService.Source("Text.AnalyzingWindowsTemporaryFiles");
                        operation.SetCancelable(false);
                        MaintenanceTaskResult administrative = elevatedAnalysis == null ? new MaintenanceTaskResult {
                            ExecutionStatus = ExecutionStatus.Failed, UserSummary = WinSereno.Localization.LocalizationService.Source("Text.ElevatedAnalysisIsUnavailable") }
                            : await elevatedAnalysis(operation, p => { if (p.State == RunnerState.Running) Summary = WinSereno.Localization.LocalizationService.Source("Text.AnalyzingWindowsTemporaryFiles"); });
                        outcome.SequenceSteps.Add(new SequenceStepResult { TaskId = ElevatedTaskCatalog.WindowsTempAnalyzeId, Result = administrative });
                        var windows = administrative.WindowsTempAnalysis;
                        if (administrative.ExecutionStatus == ExecutionStatus.Success && windows != null && windows.IsAvailable && !windows.WasCancelled && windows.AnalysisFinishedAt.HasValue)
                            SetWindowsTempAnalysis(windows);
                        else Apply(new CleanupCategoryResult { Category = CleanupCategory.WindowsTemporary, Name = WinSereno.Localization.LocalizationService.Source("Text.WindowsTemporaryFiles"), WasAnalyzed = true,
                            Path = CleanupAnalysisService.WindowsTemporaryPath, WasCancelled = administrative.ExecutionStatus == ExecutionStatus.Cancelled,
                            Information = administrative.ExecutionStatus == ExecutionStatus.Cancelled ? WinSereno.Localization.LocalizationService.Source("Text.WindowsTemporaryFilesWereNotCheckedBecauseThe") : administrative.UserSummary });
                    }
                    else Apply(previousWindows); // Automatic post-clean refresh never prompts for a second UAC or dates an old snapshot anew.
                    outcome.FindingStatus = Categories.Any(c => !c.CanClean || c.Result.IsPartial) ? FindingStatus.PartiallyCompleted : FindingStatus.Completed;
                    long total = Categories.Where(c => c.CanClean).Sum(c => c.IsRecycleBin ? c.Result.TotalBytes : c.Result.PotentiallyCleanableBytes.Value);
                    Summary = WinSereno.Localization.LocalizationService.Source("Text.AnalysisCompleted") + (outcome.FindingStatus == FindingStatus.PartiallyCompleted ? WinSereno.Localization.LocalizationService.Source("Text.PartialEstimate60") : "") +
                        CleanupCategoryViewModel.FormatBytes(total) + WinSereno.Localization.LocalizationService.Source("Text.EstimatedRecoverable");
                }
                catch (OperationCanceledException) { outcome.ExecutionStatus = ExecutionStatus.Cancelled; outcome.FindingStatus = FindingStatus.PartiallyCompleted; Summary = WinSereno.Localization.LocalizationService.Source("Text.AnalysisCancelledResultsObtainedSoFarHaveBeen"); }
                catch (Exception ex) { outcome.ExecutionStatus = ExecutionStatus.Failed; outcome.FindingStatus = FindingStatus.Unknown; Summary = WinSereno.Localization.LocalizationService.Source("Text.TheAnalysisCouldNotBeCompletedNoFiles"); if (logger != null) SystemQuery.Log(logger, "Error análisis Limpieza: " + ex); }
                finally
                {
                    watch.Stop(); IsRunning = false; hasRun = true; Raise(nameof(AnalyzeLabel)); Raise(nameof(SelectionSummary));
                    LastResult = new CleanupAnalysisResult { Categories = Array.AsReadOnly(Categories.Select(c => c.Result).ToArray()),
                        StartedAt = outcome.StartedAt, FinishedAt = DateTimeOffset.Now, Duration = watch.Elapsed, ProtectionCutoffUtc = LastResult?.ProtectionCutoffUtc ?? outcome.StartedAt.UtcDateTime.AddHours(-CleanupAnalysisService.ProtectedRecentHours),
                        WasCancelled = outcome.ExecutionStatus == ExecutionStatus.Cancelled };
                    Raise(nameof(LastResult)); AnalyzeCommand.Refresh(); Raise(nameof(CanCleanSelected));
                    if (recordHistory)
                    {
                        outcome.FinishedAt = DateTimeOffset.Now; outcome.Duration = watch.Elapsed; outcome.UserSummary = Summary;
                        var text = new StringBuilder();
                        foreach (var row in Categories) text.AppendLine(row.Name + "\n" + row.Summary + "\n" + row.Path + "\n" + row.Details + "\n" + row.ElevatedAnalysisText + "\n");
                        outcome.StdOut = text.ToString();
                        if (logger != null) SystemQuery.Log(logger, "Análisis de Limpieza | " + outcome.UserSummary + "\n" + outcome.StdOut);
                        Completed?.Invoke(this, new TaskProgress { State = RunnerState.Completed,
                            CurrentTask = new MaintenanceTask { Id = "cleanup.analyze", Name = WinSereno.Localization.LocalizationService.Source("Text.CleanupAnalysis") }, Result = outcome,
                            StartedAt = outcome.StartedAt, Elapsed = outcome.Duration, StdOut = outcome.StdOut, LastRelevantLine = outcome.UserSummary });
                    }
                }
            }
        }
        private void Apply(CleanupCategoryResult result)
        { var row = new CleanupCategoryViewModel(result); bool replaced = false;
          for (int i = 0; i < Categories.Count; i++) if (Categories[i].Result.Category == result.Category) {
              row.IsSelected = Categories[i].IsSelected; Categories[i].PropertyChanged -= SelectionChanged; Categories[i] = row; replaced = true; break;
          }
          if (!replaced) Categories.Add(row); row.PropertyChanged += SelectionChanged; Raise(nameof(SelectionSummary)); Raise(nameof(CanCleanSelected)); }
    }
}
