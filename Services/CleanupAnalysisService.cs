using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WinSereno.Models;
namespace WinSereno.Services
{
    public sealed class CleanupAnalysisService
    {
        public const int ProtectedRecentHours = 48;
        private const int ErrorExamplesPerCategory = 3;
        private readonly ISessionLogger logger;
        public CleanupAnalysisService(ISessionLogger logger) { this.logger = logger; }
        private void Log(string text) { if (logger != null) SystemQuery.Log(logger, text); }
        public static IReadOnlyList<CleanupCategoryResult> CreatePendingResults() => new[] {
            new CleanupCategoryResult { Category = CleanupCategory.UserTemporary, Name = "Temporales del usuario" },
            new CleanupCategoryResult { Category = CleanupCategory.WindowsTemporary, Name = "Temporales de Windows" },
            new CleanupCategoryResult { Category = CleanupCategory.ThumbnailCache, Name = "Caché de miniaturas del usuario" },
            new CleanupCategoryResult { Category = CleanupCategory.RecycleBin, Name = "Papelera de reciclaje" }
        };
        public Task<CleanupAnalysisResult> AnalyzeAsync(IProgress<CleanupCategoryResult> progress, CancellationToken token)
            => Task.Run(() => Analyze(progress, token));
        private CleanupAnalysisResult Analyze(IProgress<CleanupCategoryResult> progress, CancellationToken token)
        {
            var watch = Stopwatch.StartNew(); var started = DateTimeOffset.Now;
            DateTime cutoff = started.UtcDateTime.AddHours(-ProtectedRecentHours);
            var results = new List<CleanupCategoryResult>();
            Log("Inicio análisis de Limpieza SOLO LECTURA | Inicio=" + started.ToString("O") + " | Protección=" + ProtectedRecentHours + " horas | Corte UTC=" + cutoff.ToString("O"));
            foreach (var pending in CreatePendingResults())
            {
                if (token.IsCancellationRequested) break;
                var categoryWatch = Stopwatch.StartNew(); var result = pending; result.WasAnalyzed = true;
                try
                {
                    if (result.Category == CleanupCategory.RecycleBin) ReadRecycleBin(result);
                    else
                    {
                        result.Path = ResolvePath(result.Category);
                        AnalyzeDirectory(result, cutoff, result.Category != CleanupCategory.ThumbnailCache, token);
                    }
                }
                catch (OperationCanceledException) { result.WasCancelled = true; result.Information = "Consulta cancelada; se muestran solo los archivos ya consultados."; }
                catch (Exception ex) { RecordError(result, ex); result.Information = "No se pudo completar la consulta de esta categoría."; }
                result.Duration = categoryWatch.Elapsed; results.Add(result);
                Log("Limpieza categoría=" + result.Category + " | Ruta=" + result.Path + " | Disponible=" + result.IsAvailable + " | TotalBytes=" + result.TotalBytes +
                    " | Archivos/elementos=" + result.FileCount + " | PotencialBytes=" + (result.PotentiallyCleanableBytes?.ToString() ?? "no calculado") +
                    " | PotencialArchivos=" + (result.PotentiallyCleanableFileCount?.ToString() ?? "no calculado") + " | Parcial=" + result.IsPartial +
                    " | AccessDenied=" + result.AccessDeniedCount + " | Bloqueados=" + result.LockedCount + " | Otros errores=" + result.OtherErrorsCount +
                    " | Reparse omitidos=" + result.ReparsePointCount + " | Duración=" + result.Duration + " | " + result.Information);
                progress?.Report(result);
                if (result.WasCancelled) break;
            }
            watch.Stop();
            var report = new CleanupAnalysisResult { Categories = results.AsReadOnly(), StartedAt = started, FinishedAt = DateTimeOffset.Now,
                Duration = watch.Elapsed, ProtectionCutoffUtc = cutoff, WasCancelled = token.IsCancellationRequested };
            Log("Fin análisis de Limpieza SOLO LECTURA | Categorías=" + results.Count + " | Duración=" + report.Duration + " | Cancelado=" + report.WasCancelled + " | Sin borrado ni elevación");
            return report;
        }
        public CleanupCategoryResult AnalyzeWindowsTemporary() => AnalyzeWindowsTemporaryResolved(() => ResolvePath(CleanupCategory.WindowsTemporary));
        internal CleanupCategoryResult AnalyzeWindowsTemporaryResolved(Func<string> resolvePath)
        {
            var result = new CleanupCategoryResult { Category = CleanupCategory.WindowsTemporary, Name = "Temporales de Windows", WasAnalyzed = true };
            var watch = Stopwatch.StartNew();
            try { result.Path = resolvePath(); AnalyzeDirectory(result, DateTime.UtcNow.AddHours(-ProtectedRecentHours), true, CancellationToken.None); }
            catch (Exception ex) { RecordError(result, ex); result.Information = "No se pudo completar el análisis administrativo."; }
            result.Duration = watch.Elapsed;
            result.AnalysisFinishedAt = DateTimeOffset.Now;
            return result;
        }
        public CleanupCategoryResult AnalyzeUserTemporary()
        {
            var result = new CleanupCategoryResult { Category = CleanupCategory.UserTemporary, Name = "Temporales del usuario", WasAnalyzed = true };
            var watch = Stopwatch.StartNew();
            try { result.Path = ResolvePath(result.Category); AnalyzeDirectory(result, DateTime.UtcNow.AddHours(-ProtectedRecentHours), true, CancellationToken.None); }
            catch (Exception ex) { RecordError(result, ex); result.Information = "No se pudo completar el análisis."; }
            result.Duration = watch.Elapsed; return result;
        }
        public CleanupCategoryResult AnalyzeThumbnails() => AnalyzeThumbnailsResolved(() => ResolvePath(CleanupCategory.ThumbnailCache));
        internal CleanupCategoryResult AnalyzeThumbnailsResolved(Func<string> resolvePath)
        {
            var result = new CleanupCategoryResult { Category = CleanupCategory.ThumbnailCache, Name = "Caché de miniaturas del usuario", WasAnalyzed = true };
            var watch = Stopwatch.StartNew();
            try { result.Path = resolvePath(); AnalyzeDirectory(result, DateTime.UtcNow.AddHours(-ProtectedRecentHours), false, CancellationToken.None); }
            catch (Exception ex) { RecordError(result, ex); result.Information = "No se pudo completar el análisis de miniaturas."; }
            result.Duration = watch.Elapsed; return result;
        }
        public static string ThumbnailCachePath => ResolvePath(CleanupCategory.ThumbnailCache);
        public static string WindowsTemporaryPath => ResolvePath(CleanupCategory.WindowsTemporary);
        private static string ResolvePath(CleanupCategory category)
        {
            if (category == CleanupCategory.UserTemporary)
            {
                string temp = Environment.GetEnvironmentVariable("TEMP");
                if (string.IsNullOrWhiteSpace(temp)) throw new IOException("La variable TEMP del usuario no está definida.");
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(temp));
            }
            if (category == CleanupCategory.WindowsTemporary)
                return WindowsTempPathFromWindowsDirectory(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local)) throw new IOException("No se pudo resolver la carpeta local del usuario.");
            return Path.Combine(local, "Microsoft", "Windows", "Explorer");
        }
        internal static string WindowsTempPathFromWindowsDirectory(string windows)
        {
            WindowsVolumeResolver.FromWindowsPath(windows);
            return Path.Combine(windows, "Temp");
        }
        public static bool IsPotentiallyCleanable(DateTime lastWriteUtc, DateTime cutoffUtc) => lastWriteUtc > DateTime.FromFileTimeUtc(0) && lastWriteUtc < cutoffUtc;
        public static bool IsReparsePoint(FileAttributes attributes) => (attributes & FileAttributes.ReparsePoint) != 0;
        public static bool IsThumbnailCache(string name) => name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".db", StringComparison.OrdinalIgnoreCase);
        private void AnalyzeDirectory(CleanupCategoryResult result, DateTime cutoff, bool recursive, CancellationToken token)
        {
            result.PotentiallyCleanableBytes = 0; result.PotentiallyCleanableFileCount = 0;
            // Check ancestors too: a configured TEMP path must not traverse a junction on its way to the root.
            var ancestors = new Stack<DirectoryInfo>();
            for (var ancestor = new DirectoryInfo(result.Path); ancestor != null; ancestor = ancestor.Parent) ancestors.Push(ancestor);
            while (ancestors.Count > 0)
                if (IsReparsePoint(File.GetAttributes(ancestors.Pop().FullName)))
                { result.ReparsePointCount++; result.Information = "Ruta omitida porque contiene un enlace o punto de análisis."; return; }
            var directories = new Stack<DirectoryInfo>(); directories.Push(new DirectoryInfo(result.Path));
            while (directories.Count > 0)
            {
                token.ThrowIfCancellationRequested(); var directory = directories.Pop();
                try
                {
                    if (IsReparsePoint(File.GetAttributes(directory.FullName))) { result.ReparsePointCount++; continue; }
                    foreach (var entry in directory.EnumerateFileSystemInfos())
                    {
                        token.ThrowIfCancellationRequested();
                        if (string.Equals(directory.FullName.TrimEnd(Path.DirectorySeparatorChar), result.Path.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) result.IsAvailable = true;
                        try
                        {
                            // Only thumbcache_*.db in Explorer itself; never include iconcache or unrelated files/subfolders.
                            if (!recursive && !(entry is FileInfo && IsThumbnailCache(entry.Name))) continue;
                            if (IsReparsePoint(entry.Attributes)) { result.ReparsePointCount++; continue; }
                            var child = entry as DirectoryInfo;
                            if (child != null) { if (recursive) directories.Push(child); continue; }
                            var file = (FileInfo)entry;
                            // A read-only open detects exclusive locks/read denials without reading contents or changing permissions.
                            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            {
                                file.Refresh(); if (!file.Exists) throw new FileNotFoundException("El archivo desapareció durante la consulta.");
                                if (IsReparsePoint(file.Attributes)) { result.ReparsePointCount++; continue; }
                                long length = stream.Length; DateTime modified = file.LastWriteTimeUtc;
                                result.TotalBytes = checked(result.TotalBytes + length); result.FileCount++;
                                if (IsPotentiallyCleanable(modified, cutoff))
                                { result.PotentiallyCleanableBytes = checked(result.PotentiallyCleanableBytes.Value + length); result.PotentiallyCleanableFileCount++; }
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { RecordError(result, ex); }
                    }
                    if (string.Equals(directory.FullName.TrimEnd(Path.DirectorySeparatorChar), result.Path.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) result.IsAvailable = true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { RecordError(result, ex); }
            }
            if (!result.IsAvailable) { result.Information = "No se pudo enumerar la carpeta; su tamaño total es desconocido. No se considera vacía."; return; }
            result.Information = result.Category == CleanupCategory.ThumbnailCache ? "Solo thumbcache_*.db; no se incluye la caché de iconos." : "Tamaño lógico de archivos accesibles; no se leen sus contenidos.";
        }
        private void RecordError(CleanupCategoryResult result, Exception ex)
        {
            int code = ex.HResult & 0xffff;
            if (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || code == 5) result.AccessDeniedCount++;
            else if (ex is IOException && (code == 32 || code == 33)) result.LockedCount++;
            else result.OtherErrorsCount++;
            if (result.InaccessibleCount <= ErrorExamplesPerCategory) Log("Limpieza error controlado | Categoría=" + result.Category + " | " + ex.GetType().Name + " | HRESULT=0x" + ex.HResult.ToString("X8") + " | " + ex.Message);
        }
        // shellapi.h packs this structure to 1 on x86 and uses default alignment on x64.
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct RecycleInfo32 { public uint Size; public long Bytes; public long Items; }
        [StructLayout(LayoutKind.Sequential)] private struct RecycleInfo64 { public uint Size; public long Bytes; public long Items; }
        [DllImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", CharSet = CharSet.Unicode)] private static extern int QueryRecycle32(string path, ref RecycleInfo32 info);
        [DllImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", CharSet = CharSet.Unicode)] private static extern int QueryRecycle64(string path, ref RecycleInfo64 info);
        public CleanupCategoryResult AnalyzeRecycleBin()
        {
            var result = new CleanupCategoryResult { Category = CleanupCategory.RecycleBin, Name = "Papelera de reciclaje", WasAnalyzed = true };
            var watch = Stopwatch.StartNew();
            try { ReadRecycleBin(result); }
            catch (Exception ex) { RecordError(result, ex); result.Information = "No se pudo consultar la Papelera."; }
            result.Duration = watch.Elapsed; return result;
        }
        private static void ReadRecycleBin(CleanupCategoryResult result)
        {
            result.Path = "Papelera del usuario actual · todas las unidades (SHQueryRecycleBinW)";
            long bytes, items; int status;
            if (IntPtr.Size == 8)
            {
                var info = new RecycleInfo64 { Size = (uint)Marshal.SizeOf(typeof(RecycleInfo64)) };
                status = QueryRecycle64(null, ref info); bytes = info.Bytes; items = info.Items;
            }
            else
            {
                var info = new RecycleInfo32 { Size = (uint)Marshal.SizeOf(typeof(RecycleInfo32)) };
                status = QueryRecycle32(null, ref info); bytes = info.Bytes; items = info.Items;
            }
            if (status != 0)
            {
                if (status < 0) Marshal.ThrowExceptionForHR(status);
                throw new IOException("La consulta de Papelera no confirmó éxito: HRESULT=0x" + status.ToString("X8"));
            }
            if (bytes < 0 || items < 0) throw new IOException("La consulta de Papelera devolvió valores no válidos.");
            result.TotalBytes = bytes; result.FileCount = items; result.IsAvailable = true;
            result.Information = "Consulta agregada de Windows: elementos (pueden incluir carpetas). El tamaño consultado permite estimar el espacio recuperable al vaciarla.";
        }
    }
}
