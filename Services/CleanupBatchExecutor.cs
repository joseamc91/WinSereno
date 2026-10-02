using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinSereno.Models;
namespace WinSereno.Services
{
    public interface ICleanupWindowsSession
    {
        Task<MaintenanceTaskResult> RunWindowsAsync(Action<CleanupCounters> progress);
        Task<CleanupCategoryResult> ReanalyzeWindowsAsync();
        Task CloseAsync();
    }
    public sealed class CleanupBatchExecutor
    {
        public const string TaskId = "cleanup.selected";
        private readonly Func<CleanupCategory, Action<CleanupCounters>, Task<MaintenanceTaskResult>> execute;
        private readonly Func<Task<ICleanupWindowsSession>> openWindows;
        private readonly Func<CleanupCategory, Task<CleanupCategoryResult>> reanalyze;
        private readonly ISessionLogger logger;
        public CleanupBatchExecutor(ISessionLogger logger, RecycleBinCleanupService recycleBin)
        {
            this.logger = logger; openWindows = () => CleanupWindowsSession.OpenAsync(logger);
            reanalyze = category => Task.Run(() => {
                var service = new CleanupAnalysisService(logger);
                return category == CleanupCategory.UserTemporary ? service.AnalyzeUserTemporary() : category == CleanupCategory.ThumbnailCache ? service.AnalyzeThumbnails() : service.AnalyzeRecycleBin();
            });
            execute = async (category, progress) => {
                if (category == CleanupCategory.ThumbnailCache) return await ThumbnailsCleanupService.RunWithCountersAsync(logger, value => progress(CleanupCounters.FromFiles(value)));
                if (category == CleanupCategory.RecycleBin) return await recycleBin.RunAsync(null);
                if (category != CleanupCategory.UserTemporary) throw new InvalidOperationException("Categoría no permitida sin worker.");
                return await Task.Run(() => {
                    var task = UserTempCleanupService.Prepare(null);
                    var value = new UserTempCleanupService(logger).ExecuteWithCounters(UserTempCleanupService.ResolveRoot, task.ConfirmedUserTempRoot, false,
                        counters => progress(CleanupCounters.FromFiles(counters)));
                    return new MaintenanceTaskResult { UserTempCleanup = value, ExecutionStatus = value.Status == UserTempCleanupStatus.Failed ? ExecutionStatus.Failed : ExecutionStatus.Success,
                        FindingStatus = value.Status == UserTempCleanupStatus.Partial ? FindingStatus.PartiallyCompleted : value.Status == UserTempCleanupStatus.Failed ? FindingStatus.Unknown : FindingStatus.Completed,
                        StdErr = value.TechnicalDetails, StdOut = value.Summary, UserSummary = value.Summary, Duration = value.Duration,
                        CleanupCategoryAnalysis = new CleanupAnalysisService(logger).AnalyzeUserTemporary() };
                });
            };
        }
        internal CleanupBatchExecutor(Func<CleanupCategory, Action<CleanupCounters>, Task<MaintenanceTaskResult>> execute,
            Func<Task<ICleanupWindowsSession>> openWindows, Func<CleanupCategory, Task<CleanupCategoryResult>> reanalyze, ISessionLogger logger)
        { this.execute = execute; this.openWindows = openWindows; this.reanalyze = reanalyze; this.logger = logger; }
        public static IReadOnlyList<CleanupCategory> Categories(CleanupSelection selection)
        {
            if (((int)selection & ~15) != 0) throw new ArgumentException("Selección de limpieza no permitida.");
            var list = new List<CleanupCategory>();
            if (selection.HasFlag(CleanupSelection.UserTemporary)) list.Add(CleanupCategory.UserTemporary);
            if (selection.HasFlag(CleanupSelection.WindowsTemporary)) list.Add(CleanupCategory.WindowsTemporary);
            if (selection.HasFlag(CleanupSelection.Thumbnails)) list.Add(CleanupCategory.ThumbnailCache);
            if (selection.HasFlag(CleanupSelection.RecycleBin)) list.Add(CleanupCategory.RecycleBin);
            return list;
        }
        public static string Name(CleanupCategory category) => category == CleanupCategory.UserTemporary ? "Temporales del usuario" :
            category == CleanupCategory.WindowsTemporary ? "Temporales de Windows" : category == CleanupCategory.ThumbnailCache ? "Caché de miniaturas" : "Papelera";
        public static MaintenanceTask Prepare(CleanupSelection selection, IEnumerable<CleanupCategoryResult> estimates)
        {
            var categories = Categories(selection); if (categories.Count == 0) throw new InvalidOperationException("No hay categorías seleccionadas.");
            long bytes = 0; int unknown = 0;
            foreach (var category in categories)
            {
                var value = estimates.FirstOrDefault(e => e.Category == category && e.WasAnalyzed && e.IsAvailable);
                long? amount = value == null ? null : category == CleanupCategory.RecycleBin ? value.TotalBytes : value.PotentiallyCleanableBytes;
                if (amount.HasValue) bytes = checked(bytes + amount.Value); else unknown++;
            }
            bool windows = categories.Contains(CleanupCategory.WindowsTemporary), recycle = categories.Contains(CleanupCategory.RecycleBin);
            return new MaintenanceTask { Id = TaskId, Name = "Limpiar seleccionados", CleanupSelection = selection,
                TaskType = TaskType.Internal, Category = TaskCategory.Cleanup, ImpactLevel = ImpactLevel.Maintenance, RequiresElevation = windows, CanBeCancelled = false,
                ShortDescription = "Se eliminarán archivos temporales que ya no sean necesarios.",
                DetailedDescription = "Categorías seleccionadas:\n" + string.Join("\n", categories.Select(Name)) +
                    "\nEspacio recuperable total estimado: " + ViewModels.CleanupCategoryViewModel.FormatBytes(bytes) +
                    (unknown > 0 ? " conocidos; " + unknown + " categorías sin estimación disponible." : " (estimación, no garantía).") +
                    "\nLos archivos en uso, protegidos o no eliminables se omitirán. Se ejecutarán las categorías secuencialmente y se actualizarán sus cifras al terminar." +
                    (windows ? " Se solicitarán permisos de administrador antes de comenzar; la aplicación principal seguirá sin privilegios." : " No se solicitan permisos de administrador."),
                ConfirmationWarning = recycle ? "Se vaciará completamente la Papelera del usuario actual. Los elementos dejarán de poder restaurarse desde ella." : null,
                Command = "Limpieza interna de las categorías seleccionadas", Arguments = string.Join(", ", categories.Select(Name)) };
        }
        public async Task<MaintenanceTaskResult> RunAsync(MaintenanceTask task, Action<TaskProgress> progress)
        {
            var categories = Categories(task.CleanupSelection); if (categories.Count == 0) throw new InvalidOperationException("No hay categorías seleccionadas.");
            var result = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now, CleanupBatch = new CleanupBatchResult() };
            var watch = Stopwatch.StartNew(); var stdout = new StringBuilder(); var stderr = new StringBuilder();
            ICleanupWindowsSession session = null; string outputSnapshot = string.Empty, errorSnapshot = string.Empty;
            Action<string, CleanupCounters> update = (label, counters) => progress?.Invoke(new TaskProgress { CurrentTask = task, State = RunnerState.Running,
                StartedAt = result.StartedAt, Elapsed = watch.Elapsed, StepLabel = label, LastRelevantLine = counters.Summary, StdOut = outputSnapshot, StdErr = errorSnapshot });
            try
            {
                if (categories.Contains(CleanupCategory.WindowsTemporary))
                {
                    update("Solicitando permisos antes de comenzar la limpieza...", result.CleanupBatch.Totals);
                    session = await openWindows(); // No category can delete before this handshake succeeds.
                }
                for (int index = 0; index < categories.Count; index++)
                {
                    var category = categories[index]; string label = "Paso " + (index + 1) + " de " + categories.Count + " · " + Name(category);
                    var baseline = result.CleanupBatch.Totals; update(label, baseline);
                    Action<CleanupCounters> current = counters => update(label, new CleanupCounters { ReleasedBytes = baseline.ReleasedBytes + counters.ReleasedBytes,
                        RemovedElements = baseline.RemovedElements + counters.RemovedElements, SkippedElements = baseline.SkippedElements + counters.SkippedElements });
                    MaintenanceTaskResult value;
                    logger?.Write("Limpieza seleccionada | Inicio categoría=" + category);
                    try { value = category == CleanupCategory.WindowsTemporary ? await session.RunWindowsAsync(current) : await execute(category, current); }
                    catch (Exception ex) { value = new MaintenanceTaskResult { ExecutionStatus = ExecutionStatus.Failed, FindingStatus = FindingStatus.Unknown,
                        UserSummary = "No se pudo completar esta categoría.", StdErr = ex.GetType().Name + " | HRESULT=0x" + ex.HResult.ToString("X8") }; }
                    var analysis = value.CleanupCategoryAnalysis ?? value.WindowsTempAnalysis ?? value.ThumbnailAnalysis ?? value.RecycleBinAnalysis;
                    if (analysis == null) analysis = new CleanupCategoryResult { Category = category, Name = Name(category), WasAnalyzed = true,
                        OtherErrorsCount = 1, Information = "No se pudo actualizar esta categoría." };
                    var step = new CleanupBatchStep { Category = category, Result = value, Analysis = analysis };
                    result.CleanupBatch.Steps.Add(step);
                    var counts = Counts(value); result.CleanupBatch.Totals = new CleanupCounters { ReleasedBytes = baseline.ReleasedBytes + counts.ReleasedBytes,
                        RemovedElements = baseline.RemovedElements + counts.RemovedElements, SkippedElements = baseline.SkippedElements + counts.SkippedElements };
                    string state = value.ExecutionStatus != ExecutionStatus.Success ? "falló" : value.FindingStatus == FindingStatus.PartiallyCompleted || !analysis.IsAvailable ? "parcial" : "completada";
                    stdout.AppendLine(Name(category) + ": " + state + " · " + counts.Summary); stderr.Append(value.StdErr);
                    logger?.Write("Limpieza seleccionada | Categoría=" + category + " | ExecutionStatus=" + value.ExecutionStatus + " | FindingStatus=" + value.FindingStatus +
                        " | " + counts.Summary + " | Duración=" + value.Duration + " | Reanálisis disponible=" + analysis.IsAvailable + " | " + value.StdErr);
                    outputSnapshot = stdout.ToString(); errorSnapshot = stderr.ToString();
                    update(label, result.CleanupBatch.Totals);
                }
                foreach (var step in result.CleanupBatch.Steps)
                {
                    update("Actualizando resultados · " + Name(step.Category), result.CleanupBatch.Totals);
                    try { step.Analysis = step.Category == CleanupCategory.WindowsTemporary ? await session.ReanalyzeWindowsAsync() : await reanalyze(step.Category); }
                    catch (Exception ex) { step.Analysis = new CleanupCategoryResult { Category = step.Category, Name = Name(step.Category), WasAnalyzed = true, OtherErrorsCount = 1,
                        Information = "No se pudo actualizar esta categoría." }; logger?.Write("Reanálisis final falló | " + step.Category + " | " + ex.GetType().Name); }
                    logger?.Write("Reanálisis final | Categoría=" + step.Category + " | Disponible=" + step.Analysis.IsAvailable + " | TotalBytes=" + step.Analysis.TotalBytes +
                        " | Archivos=" + step.Analysis.FileCount + " | PotencialBytes=" + step.Analysis.PotentiallyCleanableBytes + " | Parcial=" + step.Analysis.IsPartial);
                    if (!step.Analysis.IsAvailable) stdout.AppendLine(Name(step.Category) + ": no se pudo actualizar el resultado final.");
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                result.ExecutionStatus = ExecutionStatus.Cancelled; result.CleanupBatch.Status = CleanupBatchStatus.Cancelled;
                logger?.Write("Elevación cancelada; no comenzó ningún borrado de la selección.");
            }
            catch (Exception ex) { result.ExecutionStatus = ExecutionStatus.Failed; stderr.AppendLine(ex.GetType().Name + " | HRESULT=0x" + ex.HResult.ToString("X8")); }
            finally
            {
                if (session != null) try { await session.CloseAsync(); } catch (Exception ex) { result.ExecutionStatus = ExecutionStatus.Failed; stderr.AppendLine("Worker: " + ex.GetType().Name); }
            }
            foreach (var category in categories.Where(c => !result.CleanupBatch.Steps.Any(s => s.Category == c)))
            {
                string reason = result.ExecutionStatus == ExecutionStatus.Cancelled ? "Elevación cancelada antes de comenzar." : "No se pudo iniciar la sesión administrativa de forma segura.";
                result.CleanupBatch.Steps.Add(new CleanupBatchStep { Category = category, WasSkipped = true, Reason = reason });
                logger?.Write("Skipped | " + category + " | " + reason); stdout.AppendLine(Name(category) + ": omitida · " + reason);
            }
            if (result.ExecutionStatus != ExecutionStatus.Cancelled)
            {
                int successes = result.CleanupBatch.Steps.Count(s => !s.WasSkipped && s.Result.ExecutionStatus == ExecutionStatus.Success);
                bool issues = result.ExecutionStatus == ExecutionStatus.Failed || result.CleanupBatch.Steps.Any(s => s.WasSkipped || s.Result.ExecutionStatus != ExecutionStatus.Success ||
                    s.Result.FindingStatus == FindingStatus.PartiallyCompleted || s.Result.FindingStatus == FindingStatus.Unknown || !s.Analysis.IsAvailable || s.Analysis.IsPartial);
                result.CleanupBatch.Status = !issues ? CleanupBatchStatus.Completed : successes > 0 || result.CleanupBatch.Totals.RemovedElements > 0 ? CleanupBatchStatus.Partial : CleanupBatchStatus.Failed;
                result.ExecutionStatus = result.ExecutionStatus == ExecutionStatus.Failed || result.CleanupBatch.Status == CleanupBatchStatus.Failed || result.CleanupBatch.Steps.Any(s => !s.WasSkipped && s.Result.ExecutionStatus == ExecutionStatus.Failed) ? ExecutionStatus.Failed : ExecutionStatus.Success;
            }
            result.FindingStatus = result.CleanupBatch.Status == CleanupBatchStatus.Completed ? FindingStatus.Completed : result.CleanupBatch.Status == CleanupBatchStatus.Partial ? FindingStatus.PartiallyCompleted : FindingStatus.Unknown;
            result.UserSummary = (result.ExecutionStatus == ExecutionStatus.Cancelled ? "Limpieza cancelada; no se inició ningún borrado." :
                result.CleanupBatch.Status == CleanupBatchStatus.Completed ? "Limpieza completada." : result.CleanupBatch.Status == CleanupBatchStatus.Partial ? "Limpieza parcial; revisa los resultados por categoría." : "No se pudo completar la limpieza.") + " · " + result.CleanupBatch.Totals.Summary;
            result.StdOut = stdout.ToString(); result.StdErr = stderr.ToString(); result.FinishedAt = DateTimeOffset.Now; result.Duration = watch.Elapsed;
            logger?.Write("Limpieza seleccionada | Resultado global=" + result.CleanupBatch.Status + " | " + result.CleanupBatch.Totals.Summary + " | Duración=" + result.Duration);
            return result;
        }
        private static CleanupCounters Counts(MaintenanceTaskResult value)
        {
            var files = value.UserTempCleanup ?? value.WindowsTempCleanup ?? value.ThumbnailCleanup;
            if (files != null) return CleanupCounters.FromFiles(files);
            var recycle = value.RecycleBinCleanup;
            return recycle == null ? new CleanupCounters() : new CleanupCounters { ReleasedBytes = recycle.ReleasedBytes ?? 0, RemovedElements = recycle.RemovedItems ?? 0,
                SkippedElements = recycle.After != null && recycle.After.IsAvailable ? recycle.After.FileCount : 0 };
        }
    }
}
