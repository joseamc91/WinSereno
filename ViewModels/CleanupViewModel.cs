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
        public bool CanAnalyzeElevated => Result.Category == CleanupCategory.WindowsTemporary && Result.WasAnalyzed && (!Result.IsAvailable || Result.IsPartial || Result.AnalysisFinishedAt.HasValue);
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
        public string Summary => !Result.WasAnalyzed ? "Aún no analizado" : !Result.IsAvailable ? "No disponible" + (Result.IsPartial ? " · Resultado parcial" : "") :
            FormatBytes(Result.TotalBytes) + " encontrados" + (Result.PotentiallyCleanableBytes.HasValue ? " · " + FormatBytes(Result.PotentiallyCleanableBytes.Value) + " potencialmente limpiables" : " · Tamaño potencialmente limpiable no calculado");
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
        private readonly OperationCoordinator operations;
        private readonly ISessionLogger logger;
        public ObservableCollection<CleanupCategoryViewModel> Categories { get; } = new ObservableCollection<CleanupCategoryViewModel>();
        public RelayCommand AnalyzeCommand { get; }
        public event EventHandler<TaskProgress> Completed;
        public bool CanChangeSelection => !operations.IsActive;
        public CleanupSelection SelectedCategories
        { get { CleanupSelection value = CleanupSelection.None; foreach (var row in Categories) if (row.IsSelected) value |= (CleanupSelection)(1 << (int)row.Result.Category); return value; } }
        public bool HasSelectedCategories => SelectedCategories != CleanupSelection.None;
        public string SelectionSummary
        {
            get {
                long bytes = 0; int count = 0, unknown = 0;
                foreach (var row in Categories) if (row.IsSelected) {
                    count++; var value = row.Result; long? estimate = !value.WasAnalyzed || !value.IsAvailable ? null : value.Category == CleanupCategory.RecycleBin ? value.TotalBytes : value.PotentiallyCleanableBytes;
                    if (estimate.HasValue) bytes += estimate.Value; else unknown++;
                }
                return count + " categorías seleccionadas · Espacio recuperable estimado: " + CleanupCategoryViewModel.FormatBytes(bytes) + (unknown > 0 ? " conocidos · " + unknown + " sin estimación" : "");
            }
        }
        private void SelectionChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        { if (e.PropertyName == nameof(CleanupCategoryViewModel.IsSelected)) { Raise(nameof(SelectedCategories)); Raise(nameof(HasSelectedCategories)); Raise(nameof(SelectionSummary)); } }
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
        public string AnalyzeLabel => hasRun ? "Actualizar análisis" : "Analizar";
        private string summary = "Análisis de solo lectura. No se ha consultado ninguna categoría todavía.";
        public string Summary { get => summary; private set => Set(ref summary, value); }
        public CleanupCategoryResult ThumbnailAnalysis
        {
            get { foreach (var category in Categories) if (category.Result.Category == CleanupCategory.ThumbnailCache) return category.Result; return null; }
        }
        public void SetCategoryAnalysis(CleanupCategoryResult result) { Apply(result); }
        public void SetRecycleBinAnalysis(CleanupCategoryResult result) { Apply(result); }
        public void SetThumbnailAnalysis(CleanupCategoryResult result) { Apply(result); }
        public CleanupAnalysisResult LastResult { get; private set; }
        public CleanupViewModel(CleanupAnalysisService service, OperationCoordinator operations, ISessionLogger logger)
            : this(service.AnalyzeAsync, operations, logger) { }
        internal CleanupViewModel(Func<IProgress<CleanupCategoryResult>, CancellationToken, Task<CleanupAnalysisResult>> analyze, OperationCoordinator operations, ISessionLogger logger)
        {
            this.analyze = analyze; this.operations = operations; this.logger = logger;
            foreach (var result in CleanupAnalysisService.CreatePendingResults()) Apply(result);
            AnalyzeCommand = new RelayCommand(async p => await AnalyzeAsync(), p => !operations.IsActive && !IsRunning);
            operations.Changed += (s, e) => { AnalyzeCommand.Refresh(); Raise(nameof(CanChangeSelection)); };
        }
        public async Task AnalyzeAsync(bool recordHistory = true)
        {
            if (operations.IsActive || IsRunning) return;
            using (var operation = operations.Begin("Análisis de Limpieza (solo lectura)", true))
            {
                var outcome = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now, ExecutionStatus = ExecutionStatus.Success };
                var watch = Stopwatch.StartNew();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                timer.Tick += (s, e) => Summary = "Analizando... · " + watch.Elapsed.ToString(@"mm\:ss") + " · Solo lectura";
                IsRunning = true; Summary = "Analizando... · 00:00 · Solo lectura"; timer.Start();
                foreach (var result in CleanupAnalysisService.CreatePendingResults()) Apply(result);
                try
                {
                    LastResult = await analyze(new Progress<CleanupCategoryResult>(Apply), operation.Token);
                    // Use the returned snapshot too: progress callbacks may still be queued on the dispatcher.
                    foreach (var result in LastResult.Categories) Apply(result);
                    if (elevatedWindowsTemp != null) Apply(elevatedWindowsTemp);
                    Summary = (LastResult.WasCancelled ? "Análisis cancelado" : "Análisis finalizado") + " en " + LastResult.Duration.TotalSeconds.ToString("0.00") + " s · No se borró ningún archivo";
                    outcome.ExecutionStatus = LastResult.WasCancelled ? ExecutionStatus.Cancelled : ExecutionStatus.Success;
                    outcome.FindingStatus = Categories.Any(c => c.Result.IsPartial || !c.Result.IsAvailable) ? FindingStatus.PartiallyCompleted : FindingStatus.Completed;
                }
                catch (Exception ex) { outcome.ExecutionStatus = ExecutionStatus.Failed; Summary = "No se pudo completar el análisis. No se borró ningún archivo."; SystemQuery.Log(logger, "Error análisis Limpieza: " + ex); }
                finally
                {
                    timer.Stop(); watch.Stop(); IsRunning = false; hasRun = true; Raise(nameof(AnalyzeLabel)); Raise(nameof(LastResult)); AnalyzeCommand.Refresh();
                    if (recordHistory)
                    {
                        outcome.FinishedAt = DateTimeOffset.Now; outcome.Duration = watch.Elapsed; outcome.UserSummary = Summary;
                        var text = new StringBuilder();
                        foreach (var row in Categories) text.AppendLine(row.Name + "\n" + row.Summary + "\n" + row.Details + "\n" + row.ElevatedAnalysisText + "\n");
                        outcome.StdOut = text.ToString();
                        Completed?.Invoke(this, new TaskProgress { State = RunnerState.Completed,
                            CurrentTask = new MaintenanceTask { Id = "cleanup.analyze", Name = "Análisis de Limpieza" }, Result = outcome,
                            StartedAt = outcome.StartedAt, Elapsed = outcome.Duration, StdOut = outcome.StdOut, LastRelevantLine = outcome.UserSummary });
                    }
                }
            }
        }
        private void Apply(CleanupCategoryResult result)
        { if (result.Category == CleanupCategory.WindowsTemporary && elevatedWindowsTemp != null) result = elevatedWindowsTemp;
          var row = new CleanupCategoryViewModel(result); bool replaced = false;
          for (int i = 0; i < Categories.Count; i++) if (Categories[i].Result.Category == result.Category) {
              row.IsSelected = Categories[i].IsSelected; Categories[i].PropertyChanged -= SelectionChanged; Categories[i] = row; replaced = true; break;
          }
          if (!replaced) Categories.Add(row); row.PropertyChanged += SelectionChanged; Raise(nameof(SelectionSummary)); }
    }
}
