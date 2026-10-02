using System;
using System.Collections.Generic;
namespace WinSereno.Models
{
    public enum CleanupCategory { UserTemporary, WindowsTemporary, ThumbnailCache, RecycleBin }
    public sealed class CleanupCategoryResult
    {
        public CleanupCategory Category { get; internal set; }
        public string Name { get; internal set; }
        public string Path { get; internal set; }
        public bool WasAnalyzed { get; internal set; }
        public bool IsAvailable { get; internal set; }
        public bool WasCancelled { get; internal set; }
        public string Information { get; internal set; }
        public long TotalBytes { get; internal set; }
        public long FileCount { get; internal set; }
        public long? PotentiallyCleanableBytes { get; internal set; }
        public long? PotentiallyCleanableFileCount { get; internal set; }
        public long AccessDeniedCount { get; internal set; }
        public long LockedCount { get; internal set; }
        public long OtherErrorsCount { get; internal set; }
        public long ReparsePointCount { get; internal set; }
        public long InaccessibleCount => AccessDeniedCount + LockedCount + OtherErrorsCount;
        public bool IsPartial => InaccessibleCount > 0 || WasCancelled;
        public TimeSpan Duration { get; internal set; }
        public DateTimeOffset? AnalysisFinishedAt { get; internal set; }
    }
    public sealed class CleanupAnalysisResult
    {
        public IReadOnlyList<CleanupCategoryResult> Categories { get; internal set; }
        public DateTimeOffset StartedAt { get; internal set; }
        public DateTimeOffset FinishedAt { get; internal set; }
        public TimeSpan Duration { get; internal set; }
        public DateTime ProtectionCutoffUtc { get; internal set; }
        public bool WasCancelled { get; internal set; }
    }
}
