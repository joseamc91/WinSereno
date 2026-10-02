using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WinSereno.Models;

namespace WinSereno.Services
{
    // Directory handles deny rename/deletion of ancestors. Deletion uses the same handle
    // whose attributes and date were checked, rather than resolving a path a second time.
    public sealed class UserTempCleanupService
    {
        public const string TaskId = "cleanup.usertemp";
        private readonly ISessionLogger logger;
        public UserTempCleanupService(ISessionLogger logger) { this.logger = logger; }
        public static string ResolveRoot()
        {
            string value = Environment.GetEnvironmentVariable("TEMP");
            if (string.IsNullOrWhiteSpace(value)) throw new IOException("TEMP no está definido.");
            string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value)).TrimEnd(Path.DirectorySeparatorChar);
            if (!Path.IsPathRooted(path) || path.Length <= Path.GetPathRoot(path).Length || path.StartsWith(@"\\"))
                throw new IOException("La ubicación TEMP no es una carpeta local segura.");
            return path;
        }
        public static MaintenanceTask Prepare(CleanupAnalysisResult analysis)
        {
            string root = ResolveRoot();
            var estimate = analysis?.Categories.FirstOrDefault(c => c.Category == CleanupCategory.UserTemporary && c.IsAvailable && string.Equals(c.Path.TrimEnd('\\'), root, StringComparison.OrdinalIgnoreCase));
            return new MaintenanceTask { Id = TaskId, Name = "Limpiar temporales del usuario", Category = TaskCategory.Cleanup,
                TaskType = TaskType.Internal, ImpactLevel = ImpactLevel.Maintenance, Command = "Limpieza interna .NET (sin procesos externos)",
                ShortDescription = "Se eliminarán archivos temporales que ya no sean necesarios.",
                DetailedDescription = "Ruta: " + root + "\nLa carpeta raíz se conserva. " +
                    "Se vuelven a comprobar fechas y atributos durante la ejecución. No se siguen enlaces, junctions ni puntos de análisis. " +
                    "Los archivos en uso, bloqueados o protegidos se omiten, sin cambiar permisos. Solo se borran carpetas vacías elegibles.\n" +
                    "Estimación del último análisis: " + (estimate?.PotentiallyCleanableBytes != null ? ViewModels.CleanupCategoryViewModel.FormatBytes(estimate.PotentiallyCleanableBytes.Value) : "no disponible") + " (no garantiza el espacio recuperado).",
                ConfirmedUserTempRoot = root, RequiresElevation = false, CanBeCancelled = false };
        }
        public UserTempCleanupResult Execute(MaintenanceTask confirmed, Action<string> progress)
            => ExecuteResolved(ResolveRoot, confirmed.ConfirmedUserTempRoot, progress);
        // Only trusted application services may choose the resolver; no path enters through UI or IPC.
        internal UserTempCleanupResult ExecuteResolved(Func<string> resolveRoot, string expectedRoot, Action<string> progress)
            => ExecuteCore(resolveRoot, expectedRoot, progress, false);
        internal UserTempCleanupResult ExecuteThumbnailsResolved(Func<string> resolveRoot, string expectedRoot, Action<string> progress)
            => ExecuteCore(resolveRoot, expectedRoot, progress, true);
        internal UserTempCleanupResult ExecuteWithCounters(Func<string> resolver, string expected, bool thumbnailsOnly, Action<UserTempCleanupResult> counters)
            => ExecuteCore(resolver, expected, null, thumbnailsOnly, counters);
        private UserTempCleanupResult ExecuteCore(Func<string> resolveRoot, string expectedRoot, Action<string> progress, bool thumbnailsOnly, Action<UserTempCleanupResult> counters = null)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = new UserTempCleanupResult();
            var guards = new List<SafeFileHandle>();
            int errorSamples = 0;
            DateTime cutoff = DateTime.UtcNow.AddHours(-CleanupAnalysisService.ProtectedRecentHours);
            Action<Exception> omitted = ex => {
                result.InaccessibleElements++;
                if (errorSamples++ < 3)
                {
                    string note = "Elemento omitido | " + ex.GetType().Name + " | HRESULT=0x" + ex.HResult.ToString("X8") + (ex is Win32Exception native ? " | Win32=" + native.NativeErrorCode : "");
                    result.TechnicalDetails += note + "\n"; logger?.Write("Limpieza TEMP: " + note);
                }
            };
            try
            {
                string root = resolveRoot();
                if (!Path.IsPathRooted(root) || root.TrimEnd(Path.DirectorySeparatorChar).Length <= Path.GetPathRoot(root).Length || root.StartsWith(@"\\")) throw new IOException("Raíz de limpieza no válida.");
                if (!string.Equals(root, expectedRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("TEMP ha cambiado desde la confirmación.");
                logger?.Write("Limpieza TEMP | ruta=" + root + " | límite UTC=" + cutoff.ToString("O") + " | Sin cambio de permisos");
                var ancestors = new Stack<string>();
                for (var directory = new DirectoryInfo(root); directory != null; directory = directory.Parent) ancestors.Push(directory.FullName);
                while (ancestors.Count > 0) guards.Add(Guard(ancestors.Pop()));
                var clock = System.Diagnostics.Stopwatch.StartNew();
                long lastUpdate = -250;
                Action<string> throttled = text => { if (clock.ElapsedMilliseconds - lastUpdate >= 250) { lastUpdate = clock.ElapsedMilliseconds; progress?.Invoke(text); counters?.Invoke(result); } };
                if (thumbnailsOnly) WalkThumbnails(root, cutoff, result, omitted, throttled);
                else Walk(root, true, cutoff, result, omitted, throttled, 0);
                result.Status = result.InaccessibleElements > 0 ? UserTempCleanupStatus.Partial :
                    result.DeletedFiles + result.DeletedDirectories == 0 ? UserTempCleanupStatus.NothingToClean : UserTempCleanupStatus.Completed;
            }
            catch (Exception ex)
            {
                result.Status = UserTempCleanupStatus.Failed;
                string note = "Limpieza no completada | " + ex.GetType().Name + " | HRESULT=0x" + ex.HResult.ToString("X8") + (ex is Win32Exception native ? " | Win32=" + native.NativeErrorCode : "");
                result.TechnicalDetails += note + "\n"; logger?.Write(note);
            }
            finally { foreach (var guard in guards) guard.Dispose(); result.Duration = watch.Elapsed; }
            logger?.Write("Resumen limpieza TEMP: " + result.Summary);
            progress?.Invoke(result.Summary); counters?.Invoke(result);
            return result;
        }
        private static void WalkThumbnails(string path, DateTime cutoff, UserTempCleanupResult result, Action<Exception> omitted, Action<string> progress)
        {
            using (var guard = Guard(path))
            foreach (string child in Directory.EnumerateFileSystemEntries(path))
            {
                if (!CleanupAnalysisService.IsThumbnailCache(Path.GetFileName(child))) continue;
                try
                {
                    var attributes = File.GetAttributes(child);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { result.ReparsePoints++; continue; }
                    if ((attributes & FileAttributes.Directory) != 0) continue;
                    Delete(child, false, cutoff, result);
                }
                catch (Exception ex) { omitted(ex); }
                progress?.Invoke(result.Summary);
            }
        }
        private static void Walk(string path, bool root, DateTime cutoff, UserTempCleanupResult result, Action<Exception> omitted, Action<string> progress, int depth)
        {
            try
            {
                if (depth > 128) throw new IOException("Profundidad de carpeta excesiva; se omite por seguridad.");
            }
            catch (Exception ex) { omitted(ex); return; }
            try
            {
                using (var guard = Guard(path))
                {
                    // Enumeration is local only; each child is checked before entering it.
                    foreach (string child in Directory.EnumerateFileSystemEntries(path))
                    {
                        try
                        {
                            var attributes = File.GetAttributes(child);
                            if ((attributes & FileAttributes.ReparsePoint) != 0) { result.ReparsePoints++; continue; }
                            if ((attributes & FileAttributes.Directory) != 0) Walk(child, false, cutoff, result, omitted, progress, depth + 1);
                            else Delete(child, false, cutoff, result);
                        }
                        catch (Exception ex) { omitted(ex); }
                        progress?.Invoke(result.Summary);
                    }
                }
                if (!root) Delete(path, true, cutoff, result);
            }
            catch (Exception ex) { if (root) throw; omitted(ex); }
        }
        private static SafeFileHandle Guard(string path)
        {
            var handle = Open(path, 0x80, 1); // Read sharing only: prevent rename/deletion and external metadata writers.
            try
            {
                var info = Info(handle);
                if ((info.Attributes & 0x400) != 0 || (info.Attributes & 0x10) == 0) throw new IOException("Carpeta sustituida o punto de análisis.");
                return handle;
            }
            catch { handle.Dispose(); throw; }
        }
        private static void Delete(string path, bool directory, DateTime cutoff, UserTempCleanupResult result)
        {
            using (var handle = Open(path, 0x10080, directory ? 1u : 0u)) // Exclusive files: skip anything currently open.
            {
                var info = Info(handle);
                if ((info.Attributes & 0x400) != 0) { result.ReparsePoints++; return; }
                if (((info.Attributes & 0x10) != 0) != directory) throw new IOException("Tipo de elemento cambiado.");
                if (!CleanupAnalysisService.IsPotentiallyCleanable(Modified(info), cutoff)) { result.ProtectedElements++; return; }
                if (directory && Directory.EnumerateFileSystemEntries(path).Any()) return;
                info = Info(handle); // Recheck immediately before deletion on this same handle.
                if ((info.Attributes & 0x400) != 0 || !CleanupAnalysisService.IsPotentiallyCleanable(Modified(info), cutoff)) { result.ProtectedElements++; return; }
                var disposition = new Disposition { Delete = true };
                if (!SetFileInformationByHandle(handle, 4, ref disposition, (uint)Marshal.SizeOf(typeof(Disposition)))) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (directory) result.DeletedDirectories++;
                else { result.DeletedFiles++; result.RecoveredBytes += ((long)info.SizeHigh << 32) | info.SizeLow; }
            }
        }
        private static DateTime Modified(FileInfoNative info) => DateTime.FromFileTimeUtc(((long)info.WriteHigh << 32) | info.WriteLow);
        private static SafeFileHandle Open(string path, uint access, uint share)
        {
            var handle = CreateFile(path, access, share, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
            return handle;
        }
        private static FileInfoNative Info(SafeFileHandle handle)
        { if (!GetFileInformationByHandle(handle, out FileInfoNative info)) throw new Win32Exception(Marshal.GetLastWin32Error()); return info; }
        [StructLayout(LayoutKind.Sequential)] private struct FileInfoNative
        { public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
        [StructLayout(LayoutKind.Sequential)] private struct Disposition { [MarshalAs(UnmanagedType.U1)] public bool Delete; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfoNative info);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int type, ref Disposition info, uint size);
    }
}
