using System;
using System.Collections.Generic;
namespace WinSereno.Models
{
    [Flags] public enum CleanupSelection { None = 0, UserTemporary = 1, WindowsTemporary = 2, Thumbnails = 4, RecycleBin = 8 }
    public enum CleanupBatchStatus { Completed, Partial, Failed, Cancelled }
    public sealed class CleanupCounters
    {
        public long ReleasedBytes { get; set; }
        public long RemovedElements { get; set; }
        public long SkippedElements { get; set; }
        public static CleanupCounters FromFiles(UserTempCleanupResult value) => new CleanupCounters { ReleasedBytes = value.RecoveredBytes,
            RemovedElements = value.DeletedFiles + value.DeletedDirectories, SkippedElements = value.ProtectedElements + value.InaccessibleElements + value.ReparsePoints };
        private static string FormatBytes(long bytes)
        { string[] units = { "B", "KB", "MB", "GB", "TB" }; double value = bytes; int unit = 0; while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; } return value.ToString("0.#") + " " + units[unit]; }
        public string Summary => "Espacio recuperado: " + FormatBytes(ReleasedBytes) + " · Elementos eliminados: " + RemovedElements + " · Omitidos: " + SkippedElements;
    }
    public sealed class CleanupBatchStep
    {
        public CleanupCategory Category { get; internal set; }
        public MaintenanceTaskResult Result { get; internal set; }
        public CleanupCategoryResult Analysis { get; internal set; }
        public bool WasSkipped { get; internal set; }
        public string Reason { get; internal set; }
    }
    public sealed class CleanupBatchResult
    {
        public CleanupBatchStatus Status { get; internal set; }
        public IList<CleanupBatchStep> Steps { get; } = new List<CleanupBatchStep>();
        public CleanupCounters Totals { get; internal set; } = new CleanupCounters();
    }
}
