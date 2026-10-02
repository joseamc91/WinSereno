using System;
namespace WinSereno.Models
{
    public enum RecycleBinCleanupStatus { Completed, Partial, Failed, Cancelled, Unknown }
    public sealed class RecycleBinCleanupResult
    {
        public RecycleBinCleanupStatus Status { get; internal set; }
        public CleanupCategoryResult Before { get; internal set; }
        public CleanupCategoryResult After { get; internal set; }
        public long? RemovedItems { get; internal set; }
        public long? ReleasedBytes { get; internal set; }
        public int? NativeHResult { get; internal set; }
        public bool NativeCallStarted { get; internal set; }
        public TimeSpan Duration { get; internal set; }
    }
}
