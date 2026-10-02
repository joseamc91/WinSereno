using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WinSereno.Models;
namespace WinSereno.Services
{
    public sealed class RecycleBinCleanupService
    {
        public const string TaskId = "cleanup.recyclebin";
        private const uint ShellFlags = 0x1 | 0x2 | 0x4; // NOCONFIRMATION, NOPROGRESSUI, NOSOUND.
        private readonly Func<CleanupCategoryResult> query;
        private readonly Func<int> empty;
        private readonly ISessionLogger logger;
        public RecycleBinCleanupService(ISessionLogger logger)
            : this(() => new CleanupAnalysisService(logger).AnalyzeRecycleBin(), EmptyNative, logger) { }
        // Controlled providers for in-memory tests; neither is supplied by UI or IPC.
        internal RecycleBinCleanupService(Func<CleanupCategoryResult> query, Func<int> empty, ISessionLogger logger)
        { this.query = query; this.empty = empty; this.logger = logger; }
        public Task<CleanupCategoryResult> QueryAsync() => Task.Run(() => ReadSafe());
        public static MaintenanceTask Prepare(CleanupCategoryResult current)
        {
            if (current == null || !current.IsAvailable) throw new InvalidOperationException("No se pudo consultar la Papelera antes de confirmar.");
            return new MaintenanceTask { Id = TaskId, Name = "Vaciar Papelera", Category = TaskCategory.Cleanup,
                ImpactLevel = ImpactLevel.Maintenance, TaskType = TaskType.Internal, RequiresElevation = false, CanBeCancelled = false,
                ShortDescription = "Vacía toda la Papelera del usuario actual, en todas las unidades.",
                DetailedDescription = "Elementos actuales: " + current.FileCount + "\nTamaño total: " + ViewModels.CleanupCategoryViewModel.FormatBytes(current.TotalBytes) +
                    "\nSe vaciará toda la Papelera del usuario actual. " +
                    "Los archivos dejarán de poder restaurarse desde la Papelera. No se solicita UAC, no se cambian permisos y no se muestra una segunda confirmación del Shell. " +
                    "Se consulta de nuevo antes y después de vaciar y se informa si quedan elementos. Las cifras pueden variar si otras aplicaciones cambian la Papelera.",
                ConfirmationWarning = "Los archivos dejarán de poder restaurarse desde la Papelera.",
                Command = "SHEmptyRecycleBinW (Papelera del usuario actual, todas las unidades)",
                Arguments = "Ruta nula interna; SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND" };
        }
        private CleanupCategoryResult ReadSafe()
        {
            try
            {
                var value = query();
                if (value == null || value.Category != CleanupCategory.RecycleBin || value.FileCount < 0 || value.TotalBytes < 0)
                    throw new InvalidOperationException("Consulta de Papelera inválida.");
                return value;
            }
            catch (Exception ex)
            {
                logger?.Write("Consulta Papelera fallida | " + ex.GetType().Name + " | HRESULT=0x" + ex.HResult.ToString("X8"));
                return new CleanupCategoryResult { Category = CleanupCategory.RecycleBin, Name = "Papelera de reciclaje", WasAnalyzed = true,
                    Path = "Papelera del usuario actual · todas las unidades", OtherErrorsCount = 1, Information = "No se pudo consultar la Papelera." };
            }
        }
        public Task<MaintenanceTaskResult> RunAsync(Action<string> progress) => Task.Run(() => RunCore(progress));
        private MaintenanceTaskResult RunCore(Action<string> progress)
        {
            var watch = Stopwatch.StartNew(); var result = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now };
            var value = new RecycleBinCleanupResult { Status = RecycleBinCleanupStatus.Unknown };
            result.RecycleBinCleanup = value;
            value.Before = ReadSafe();
            if (!value.Before.IsAvailable)
            {
                value.Status = RecycleBinCleanupStatus.Failed; value.After = value.Before;
                result.ExecutionStatus = ExecutionStatus.Failed;
                result.UserSummary = "No se pudo consultar la Papelera; no se inició el vaciado.";
            }
            else
            {
                bool succeeded = true;
                if (value.Before.FileCount != 0 || value.Before.TotalBytes != 0)
                {
                    progress?.Invoke("Vaciando toda la Papelera del usuario actual..."); value.NativeCallStarted = true;
                    try { value.NativeHResult = empty(); succeeded = value.NativeHResult == 0; }
                    catch (Exception ex) { succeeded = false; result.StdErr = ex.GetType().Name + " | HRESULT=0x" + ex.HResult.ToString("X8"); }
                }
                progress?.Invoke("Consultando elementos restantes en la Papelera...");
                value.After = ReadSafe();
                if (value.After.IsAvailable)
                {
                    value.RemovedItems = Math.Max(0, value.Before.FileCount - value.After.FileCount);
                    value.ReleasedBytes = Math.Max(0, value.Before.TotalBytes - value.After.TotalBytes);
                }
                if (!succeeded)
                {
                    bool cancelled = value.NativeHResult == unchecked((int)0x800704C7);
                    value.Status = cancelled ? RecycleBinCleanupStatus.Cancelled : RecycleBinCleanupStatus.Failed;
                    result.ExecutionStatus = cancelled ? ExecutionStatus.Cancelled : ExecutionStatus.Failed;
                    result.UserSummary = cancelled ? "Windows canceló el vaciado de la Papelera." : "Windows no confirmó el vaciado correcto de la Papelera.";
                    result.StdErr += "\nSHEmptyRecycleBinW HRESULT=" + (value.NativeHResult.HasValue ? "0x" + value.NativeHResult.Value.ToString("X8") : "no disponible");
                }
                else if (!value.After.IsAvailable)
                {
                    value.Status = RecycleBinCleanupStatus.Unknown; result.ExecutionStatus = ExecutionStatus.Success;
                    result.UserSummary = "La llamada terminó correctamente, pero no se pudo comprobar el estado posterior de la Papelera.";
                }
                else
                {
                    value.Status = value.After.FileCount == 0 && value.After.TotalBytes == 0 ? RecycleBinCleanupStatus.Completed : RecycleBinCleanupStatus.Partial;
                    result.ExecutionStatus = ExecutionStatus.Success;
                    result.UserSummary = value.Status == RecycleBinCleanupStatus.Completed ? "Papelera vaciada." : "Vaciado parcial: permanecen elementos en la Papelera.";
                }
            }
            result.RecycleBinAnalysis = value.After;
            result.FindingStatus = value.Status == RecycleBinCleanupStatus.Completed ? FindingStatus.Completed :
                value.Status == RecycleBinCleanupStatus.Partial ? FindingStatus.PartiallyCompleted : FindingStatus.Unknown;
            value.Duration = watch.Elapsed; result.Duration = value.Duration; result.FinishedAt = DateTimeOffset.Now;
            result.UserSummary += " · Elementos eliminados: " + (value.RemovedItems?.ToString() ?? "no disponible") +
                " · Espacio liberado: " + (value.ReleasedBytes.HasValue ? ViewModels.CleanupCategoryViewModel.FormatBytes(value.ReleasedBytes.Value) : "no disponible") +
                " · Elementos restantes: " + (value.After.IsAvailable ? value.After.FileCount.ToString() : "no disponible") + " · Duración: " + value.Duration.TotalSeconds.ToString("0.00") + " s";
            result.StdOut = result.UserSummary;
            logger?.Write("Papelera | Antes elementos=" + value.Before.FileCount + " | Antes bytes=" + value.Before.TotalBytes +
                " | Después disponible=" + value.After.IsAvailable + " | Después elementos=" + value.After.FileCount + " | Después bytes=" + value.After.TotalBytes +
                " | Eliminados (variación neta)=" + value.RemovedItems + " | LiberadosBytes (variación neta)=" + value.ReleasedBytes +
                " | API iniciada=" + value.NativeCallStarted + " | HRESULT=" + (value.NativeHResult.HasValue ? "0x" + value.NativeHResult.Value.ToString("X8") : "no aplicable") +
                " | Resultado=" + value.Status + " | Duración=" + value.Duration);
            return result;
        }
        private static int EmptyNative()
        {
            int code = 0; Exception failure = null;
            var thread = new Thread(() => {
                bool initialized = false;
                try { int hr = CoInitializeEx(IntPtr.Zero, 2); if (hr < 0) Marshal.ThrowExceptionForHR(hr); initialized = true;
                    code = SHEmptyRecycleBinW(IntPtr.Zero, null, ShellFlags); }
                catch (Exception ex) { failure = ex; }
                finally { if (initialized) CoUninitialize(); }
            }) { IsBackground = true, Name = "WinSereno Papelera" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (failure != null) throw failure;
            return code;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int SHEmptyRecycleBinW(IntPtr window, string rootPath, uint flags);
        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
    }
}
