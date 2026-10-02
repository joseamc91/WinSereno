using System;
using System.Diagnostics;
using System.Threading.Tasks;
using WinSereno.Models;
namespace WinSereno.Services
{
    internal static class ThumbnailsCleanupService
    {
        public const string TaskId = "cleanup.thumbnails";
        public static MaintenanceTask Prepare(CleanupCategoryResult estimate)
        {
            string root = CleanupAnalysisService.ThumbnailCachePath;
            bool valid = estimate != null && estimate.WasAnalyzed && estimate.IsAvailable &&
                string.Equals(estimate.Path, root, StringComparison.OrdinalIgnoreCase);
            return new MaintenanceTask { Id = TaskId, Name = "Limpiar caché de miniaturas", Category = TaskCategory.Cleanup,
                TaskType = TaskType.Internal, ImpactLevel = ImpactLevel.Maintenance, RequiresElevation = false, CanBeCancelled = false,
                ConfirmedThumbnailRoot = root, Command = "Limpieza interna .NET de thumbcache_*.db", Arguments = "Solo archivos de la carpeta Explorer; archivos en uso o protegidos se omiten",
                ShortDescription = "Elimina únicamente archivos de caché de miniaturas elegibles que no estén en uso.",
                DetailedDescription = "Ruta: " + root + "\nSolo se consideran thumbcache_*.db de esta carpeta, sin entrar en subcarpetas ni borrar carpetas. " +
                    "No se borran cachés de iconos ni otros archivos. Los archivos en uso o protegidos se omitirán. " +
                    "Los archivos bloqueados, en uso o inaccesibles se omiten; no se siguen puntos de análisis ni se cambian permisos. " +
                    "No se cierra ni reinicia explorer.exe, ni se matan procesos. Windows recreará automáticamente las miniaturas cuando sean necesarias. " +
                    "Se reanaliza únicamente esta categoría al terminar, sin UAC.\n" + (valid ?
                        "Último análisis: " + ViewModels.CleanupCategoryViewModel.FormatBytes(estimate.TotalBytes) + " detectados · " + estimate.FileCount +
                        " archivos accesibles" + (estimate.IsPartial ? " · Resultado parcial" : "") + "\nPotencialmente limpiable: " +
                        (estimate.PotentiallyCleanableBytes.HasValue ? ViewModels.CleanupCategoryViewModel.FormatBytes(estimate.PotentiallyCleanableBytes.Value) : "no disponible") +
                        " (estimación, no garantía)." : "Tamaño detectado, potencialmente limpiable y número de archivos: no disponibles; aún no hay un análisis válido.") };
        }
        public static Task<MaintenanceTaskResult> RunAsync(MaintenanceTask confirmed, ISessionLogger logger, Action<string> progress)
            => Task.Run(() => RunCore(
                () => new UserTempCleanupService(logger).ExecuteThumbnailsResolved(() => CleanupAnalysisService.ThumbnailCachePath,
                    confirmed.ConfirmedThumbnailRoot, progress),
                () => new CleanupAnalysisService(logger).AnalyzeThumbnails(), progress, logger));
        internal static Task<MaintenanceTaskResult> RunWithCountersAsync(ISessionLogger logger, Action<UserTempCleanupResult> counters)
            => Task.Run(() => RunCore(
                () => new UserTempCleanupService(logger).ExecuteWithCounters(() => CleanupAnalysisService.ThumbnailCachePath,
                    CleanupAnalysisService.ThumbnailCachePath, true, counters),
                () => new CleanupAnalysisService(logger).AnalyzeThumbnails(), null, logger));
        // Tests provide controlled providers here, never a route or pattern from the UI.
        internal static MaintenanceTaskResult RunCore(Func<UserTempCleanupResult> cleanup, Func<CleanupCategoryResult> analyze,
            Action<string> progress, ISessionLogger logger)
        {
            var watch = Stopwatch.StartNew();
            var result = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now };
            try { result.ThumbnailCleanup = cleanup(); }
            catch (Exception ex) { result.ThumbnailCleanup = new UserTempCleanupResult { Status = UserTempCleanupStatus.Failed, TechnicalDetails = ex.GetType().Name + " | HRESULT=0x" + ex.HResult.ToString("X8") }; }
            progress?.Invoke("Reanalizando caché de miniaturas... " + result.ThumbnailCleanup.Summary);
            try { result.ThumbnailAnalysis = analyze(); }
            catch (Exception ex)
            {
                result.ThumbnailAnalysis = new CleanupCategoryResult { Category = CleanupCategory.ThumbnailCache, Name = "Caché de miniaturas del usuario",
                    Path = CleanupAnalysisService.ThumbnailCachePath, WasAnalyzed = true, OtherErrorsCount = 1, Information = "No se pudo completar el reanálisis de miniaturas." };
                logger?.Write("Error reanálisis miniaturas | " + ex.GetType().Name + " | HRESULT=0x" + ex.HResult.ToString("X8"));
            }
            var value = result.ThumbnailCleanup;
            result.ExecutionStatus = value.Status == UserTempCleanupStatus.Failed ? ExecutionStatus.Failed : ExecutionStatus.Success;
            result.FindingStatus = value.Status == UserTempCleanupStatus.Failed ? FindingStatus.Unknown : value.Status == UserTempCleanupStatus.Partial ? FindingStatus.PartiallyCompleted : FindingStatus.Completed;
            result.UserSummary = "Miniaturas · " + value.Summary + " · Duración limpieza: " + value.Duration.TotalSeconds.ToString("0.00") + " s · Reanálisis: " +
                (!result.ThumbnailAnalysis.IsAvailable ? "no disponible" : result.ThumbnailAnalysis.IsPartial ? "parcial" : "completado");
            result.StdOut = result.UserSummary; result.StdErr = value.TechnicalDetails;
            result.FinishedAt = DateTimeOffset.Now; result.Duration = watch.Elapsed;
            var after = result.ThumbnailAnalysis;
            logger?.Write("Reanálisis miniaturas | TotalBytes=" + after.TotalBytes + " | Archivos=" + after.FileCount + " | PotencialBytes=" + after.PotentiallyCleanableBytes +
                " | PotencialArchivos=" + after.PotentiallyCleanableFileCount + " | Disponible=" + after.IsAvailable + " | Parcial=" + after.IsPartial +
                " | AccessDenied=" + after.AccessDeniedCount + " | Bloqueados=" + after.LockedCount + " | Otros errores=" + after.OtherErrorsCount + " | Duración=" + after.Duration);
            return result;
        }
    }
}
