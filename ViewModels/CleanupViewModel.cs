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
        public string RecoverableEstimate => "Espacio recuperable estimado: " + (!Result.WasAnalyzed || !Result.IsAvailable ? "no disponible" :
            Result.Category == CleanupCategory.RecycleBin ? FormatBytes(Result.TotalBytes) : Result.PotentiallyCleanableBytes.HasValue ? FormatBytes(Result.PotentiallyCleanableBytes.Value) : "no disponible");
        public bool CanClean => Result.WasAnalyzed && Result.IsAvailable && !Result.WasCancelled && Result.TotalBytes >= 0 &&
            (IsRecycleBin || (!string.IsNullOrWhiteSpace(Result.Path) && Result.PotentiallyCleanableBytes.HasValue &&
                Result.PotentiallyCleanableBytes >= 0 && Result.PotentiallyCleanableBytes <= Result.TotalBytes)) &&
            (!IsWindowsTemporary || Result.AnalysisFinishedAt.HasValue);
        public string Status => !Result.WasAnalyzed ? "Aún no analizado" : !Result.IsAvailable || Result.WasCancelled ? "No comprobado" : Result.IsPartial ? "Resultado parcial" : "Analizado";
        public bool HasElevatedAnalysisTime => IsWindowsTemporary && Result.AnalysisFinishedAt.HasValue;
        public string ElevatedAnalysisText => HasElevatedAnalysisTime ? FormatAnalysisTime(Result.AnalysisFinishedAt.Value) : "";
        internal static string FormatAnalysisTime(DateTimeOffset finished)
            => "Último análisis con permisos: " + finished.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
        public bool IsWindowsTemporary => Result.Category == CleanupCategory.WindowsTemporary;
        public bool IsRecycleBin => Result.Category == CleanupCategory.RecycleBin;
        public bool IsThumbnailCache => Result.Category == CleanupCategory.ThumbnailCache;
        public bool IsUserTemporary => Result.Category == CleanupCategory.UserTemporary;
        public string Name => Result.Name;
        public string Path => Result.Path;
        public string DisplayPath => IsRecycleBin ? "Papelera del usuario actual." : Path;
        public bool HasAmounts => Result.WasAnalyzed && Result.IsAvailable && !Result.WasCancelled;
        public string Summary => !Result.WasAnalyzed ? "Aún no analizado" : !Result.IsAvailable || Result.WasCancelled ? "No comprobado" :
            IsRecycleBin ? FormatBytes(Result.TotalBytes) + " en la Papelera" :
            FormatBytes(Result.TotalBytes) + " encontrados" + (Result.PotentiallyCleanableBytes.HasValue ? " · " + FormatBytes(Result.PotentiallyCleanableBytes.Value) + " potencialmente limpiables" : " · Estimación no disponible");
        public string Details => !Result.WasAnalyzed ? "Pulsa Analizar para consultar esta categoría." :
            Result.FileCount.ToString("N0") + (Result.Category == CleanupCategory.RecycleBin ? " elementos" : " archivos accesibles") +
            (Result.IsAvailable && Result.PotentiallyCleanableFileCount.HasValue ? " · " + Result.PotentiallyCleanableFileCount.Value.ToString("N0") + " potencialmente limpiables" : "") +
            (Result.IsPartial ? " · Resultado parcial: " + Result.InaccessibleCount + " elementos/consultas inaccesibles (" + Result.AccessDeniedCount + " accesos denegados, " + Result.LockedCount + " bloqueados, " + Result.OtherErrorsCount + " otros errores)" : "") +
            (Result.ReparsePointCount > 0 ? " · " + Result.ReparsePointCount + " enlaces/puntos de análisis omitidos" : "") + "\n" + Result.Information;
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
                var selected = count + (count == 1 ? " categoría marcada" : " categorías marcadas");
                if (!hasRun) return selected;
                if (count == 0) return selected;
                if (unknown == count) return selected + " · Estimación no disponible";
                bool partial = unknown > 0 || Categories.Any(c => c.IsSelected && c.Result.IsPartial);
                return selected + (partial ? " · Estimación parcial: " : " · Espacio recuperable estimado: ") + CleanupCategoryViewModel.FormatBytes(bytes);
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
        public string AnalyzeLabel => hasRun ? "Analizar de nuevo" : "Analizar";
        private string summary = "Aún no se ha realizado un análisis.";
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
            using (var operation = operations.Begin("Análisis de Limpieza", true))
            {
                var outcome = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now, ExecutionStatus = ExecutionStatus.Success };
                var watch = Stopwatch.StartNew();
                var previousWindows = Categories.Single(c => c.IsWindowsTemporary).Result;
                IsRunning = true; Summary = "Analizando archivos..."; LastResult = null;
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
                        Summary = "Analizando Temporales de Windows...";
                        operation.SetCancelable(false);
                        MaintenanceTaskResult administrative = elevatedAnalysis == null ? new MaintenanceTaskResult {
                            ExecutionStatus = ExecutionStatus.Failed, UserSummary = "No está disponible el análisis administrativo." }
                            : await elevatedAnalysis(operation, p => { if (p.State == RunnerState.Running) Summary = "Analizando Temporales de Windows..."; });
                        outcome.SequenceSteps.Add(new SequenceStepResult { TaskId = ElevatedTaskCatalog.WindowsTempAnalyzeId, Result = administrative });
                        var windows = administrative.WindowsTempAnalysis;
                        if (administrative.ExecutionStatus == ExecutionStatus.Success && windows != null && windows.IsAvailable && !windows.WasCancelled && windows.AnalysisFinishedAt.HasValue)
                            SetWindowsTempAnalysis(windows);
                        else Apply(new CleanupCategoryResult { Category = CleanupCategory.WindowsTemporary, Name = "Temporales de Windows", WasAnalyzed = true,
                            Path = CleanupAnalysisService.WindowsTemporaryPath, WasCancelled = administrative.ExecutionStatus == ExecutionStatus.Cancelled,
                            Information = administrative.ExecutionStatus == ExecutionStatus.Cancelled ? "No se comprobaron los temporales de Windows porque se cancelaron los permisos de administrador." : administrative.UserSummary });
                    }
                    else Apply(previousWindows); // Automatic post-clean refresh never prompts for a second UAC or dates an old snapshot anew.
                    outcome.FindingStatus = Categories.Any(c => !c.CanClean || c.Result.IsPartial) ? FindingStatus.PartiallyCompleted : FindingStatus.Completed;
                    long total = Categories.Where(c => c.CanClean).Sum(c => c.IsRecycleBin ? c.Result.TotalBytes : c.Result.PotentiallyCleanableBytes.Value);
                    Summary = "Análisis finalizado · " + (outcome.FindingStatus == FindingStatus.PartiallyCompleted ? "Estimación parcial: " : "") +
                        CleanupCategoryViewModel.FormatBytes(total) + " recuperables estimados";
                }
                catch (OperationCanceledException) { outcome.ExecutionStatus = ExecutionStatus.Cancelled; outcome.FindingStatus = FindingStatus.PartiallyCompleted; Summary = "Análisis cancelado; se conservan los resultados obtenidos."; }
                catch (Exception ex) { outcome.ExecutionStatus = ExecutionStatus.Failed; outcome.FindingStatus = FindingStatus.Unknown; Summary = "No se pudo completar el análisis. No se borró ningún archivo."; if (logger != null) SystemQuery.Log(logger, "Error análisis Limpieza: " + ex); }
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
                            CurrentTask = new MaintenanceTask { Id = "cleanup.analyze", Name = "Análisis de Limpieza" }, Result = outcome,
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
