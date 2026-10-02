using System;

namespace WinSereno.Models
{
    public sealed class MaintenanceTaskResult
    {
        public CleanupCategoryResult CleanupCategoryAnalysis { get; internal set; }
        public CleanupBatchResult CleanupBatch { get; internal set; }
        public RecycleBinCleanupResult RecycleBinCleanup { get; internal set; }
        public CleanupCategoryResult RecycleBinAnalysis { get; internal set; }
        public UserTempCleanupResult ThumbnailCleanup { get; internal set; }
        public CleanupCategoryResult ThumbnailAnalysis { get; internal set; }
        public UserTempCleanupResult WindowsTempCleanup { get; internal set; }
        public CleanupCategoryResult WindowsTempAnalysis { get; internal set; }
        public UserTempCleanupResult UserTempCleanup { get; internal set; }
        public System.Collections.Generic.IList<SequenceStepResult> SequenceSteps { get; } = new System.Collections.Generic.List<SequenceStepResult>();
        public ExecutionStatus ExecutionStatus { get; set; } = ExecutionStatus.Unknown;
        // Independent of ExitCode: a successful execution can report RepairRequired.
        public FindingStatus FindingStatus { get; set; } = FindingStatus.Unknown;
        public string StdOut { get; set; } = "";
        public string StdErr { get; set; } = "";
        public int? ExitCode { get; set; }
        // Set only after Process.Start succeeds (or its existing IPC notification is received).
        public bool CommandStarted { get; internal set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset FinishedAt { get; set; }
        public TimeSpan Duration { get; set; }
        public string UserSummary { get; set; }
        public bool RequiresRestart { get; set; }
    }

    public sealed class TaskProgress
    {
        public string StepLabel { get; set; }
        public MaintenanceTask CurrentTask { get; set; }
        public RunnerState State { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public TimeSpan Elapsed { get; set; }
        public string LastRelevantLine { get; set; }
        public string StdOut { get; set; } = "";
        public string StdErr { get; set; } = "";
        // Null means the tool does not expose a real percentage.
        public double? Percentage { get; set; }
        public MaintenanceTaskResult Result { get; set; }
    }
}
