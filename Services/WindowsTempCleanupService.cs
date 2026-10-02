using System;
using System.Diagnostics;
using System.Threading.Tasks;
using WinSereno.Models;
namespace WinSereno.Services
{
    internal static class WindowsTempCleanupService
    {
        public static MaintenanceTask Prepare(CleanupCategoryResult estimate)
        {
            var task = ElevatedTaskCatalog.Get(ElevatedTaskCatalog.WindowsTempCleanupId);
            bool valid = estimate != null && estimate.WasAnalyzed && estimate.IsAvailable &&
                string.Equals(estimate.Path, CleanupAnalysisService.WindowsTemporaryPath, StringComparison.OrdinalIgnoreCase);
            task.DetailedDescription += "\nÚltimo análisis elevado de esta sesión: " + (valid ?
                ViewModels.CleanupCategoryViewModel.FormatBytes(estimate.TotalBytes) + " encontrados · " + estimate.FileCount + " archivos · " +
                (estimate.IsPartial ? "resultado parcial" : "completo") + "\nEspacio potencialmente recuperable estimado: " +
                ViewModels.CleanupCategoryViewModel.FormatBytes(estimate.PotentiallyCleanableBytes ?? 0) + " (estimación, no garantía)." :
                "no disponible. Espacio potencialmente recuperable: no estimado.");
            return task;
        }
        // The worker supplies no path from IPC. Both providers resolve the same fixed system location.
        public static Task<MaintenanceTaskResult> RunAsync(Action<string> progress)
            => Task.Run(() => RunCore(
                () => new UserTempCleanupService(null).ExecuteResolved(() => CleanupAnalysisService.WindowsTemporaryPath,
                    CleanupAnalysisService.WindowsTemporaryPath, progress),
                () => new CleanupAnalysisService(null).AnalyzeWindowsTemporary(), progress));
        internal static Task<MaintenanceTaskResult> RunWithCountersAsync(Action<UserTempCleanupResult> counters)
            => Task.Run(() => RunCore(
                () => new UserTempCleanupService(null).ExecuteWithCounters(() => CleanupAnalysisService.WindowsTemporaryPath,
                    CleanupAnalysisService.WindowsTemporaryPath, false, counters),
                () => new CleanupAnalysisService(null).AnalyzeWindowsTemporary(), null));
        // Pure orchestration seam for controlled providers in tests; never exposed through the worker protocol.
        internal static MaintenanceTaskResult RunCore(Func<UserTempCleanupResult> cleanup, Func<CleanupCategoryResult> analyze, Action<string> progress)
        {
            var watch = Stopwatch.StartNew();
            var result = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now };
            try { result.WindowsTempCleanup = cleanup(); }
            catch (Exception ex) { result.WindowsTempCleanup = new UserTempCleanupResult { Status = UserTempCleanupStatus.Failed, TechnicalDetails = ex.GetType().Name + " | HRESULT=0x" + ex.HResult.ToString("X8") }; }
            progress?.Invoke("Reanalizando temporales de Windows dentro del mismo worker... " + result.WindowsTempCleanup.Summary);
            try { result.WindowsTempAnalysis = analyze(); }
            catch
            {
                result.WindowsTempAnalysis = new CleanupCategoryResult { Category = CleanupCategory.WindowsTemporary,
                    Name = "Temporales de Windows", Path = CleanupAnalysisService.WindowsTemporaryPath, WasAnalyzed = true,
                    OtherErrorsCount = 1, Information = "No se pudo completar el reanálisis posterior." };
            }
            result.FinishedAt = DateTimeOffset.Now; result.Duration = watch.Elapsed;
            WindowsTempCleanupProtocol.Apply(result);
            return result;
        }
    }
}
