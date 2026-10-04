using System;
using System.Globalization;
using System.Text;

namespace WinSereno.Models
{
    // Keep values and text, not references to the runner's mutable result or its steps.
    public sealed class ActionHistoryEntry
    {
        public string TaskId { get; }
        public string Name { get; }
        public DateTimeOffset StartedAt { get; }
        public DateTimeOffset FinishedAt { get; }
        public TimeSpan Duration { get; }
        public ExecutionStatus ExecutionStatus { get; }
        public FindingStatus FindingStatus { get; }
        public bool RequiresRestart { get; }
        public int? ExitCode { get; }
        public string Summary { get; }
        public string StdOut { get; }
        public string StdErr { get; }
        private readonly string details;
        public string TimeText => FinishedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
        public string DurationText => Duration.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture) + " s";
        public string StatusText
        {
            get
            {
                if (ExecutionStatus == ExecutionStatus.Cancelled) return WinSereno.Localization.LocalizationService.Source("Text.Cancelled");
                if (ExecutionStatus == ExecutionStatus.Failed) return WinSereno.Localization.LocalizationService.Source("Text.Failed701");
                switch (FindingStatus)
                {
                    case FindingStatus.Healthy: return WinSereno.Localization.LocalizationService.Source("Text.Healthy");
                    case FindingStatus.Repaired: return WinSereno.Localization.LocalizationService.Source("Text.Repaired");
                    case FindingStatus.Completed: return WinSereno.Localization.LocalizationService.Source("Text.Completed");
                    case FindingStatus.PartiallyCompleted: return WinSereno.Localization.LocalizationService.Source("Text.Partial");
                    case FindingStatus.Attention:
                    case FindingStatus.RepairRequired: return WinSereno.Localization.LocalizationService.Source("Text.Attention");
                    case FindingStatus.RestartRequired: return WinSereno.Localization.LocalizationService.Source("Text.RestartRequired");
                    case FindingStatus.RepairFailed:
                    case FindingStatus.Unrepairable:
                    case FindingStatus.SourceFilesNotFound:
                    case FindingStatus.ScanFailed:
                    case FindingStatus.Failed: return WinSereno.Localization.LocalizationService.Source("Text.Failed701");
                    default: return WinSereno.Localization.LocalizationService.Source("Text.NotChecked");
                }
            }
        }
        public string StateText => StatusText + (RequiresRestart && FindingStatus != FindingStatus.RestartRequired ? WinSereno.Localization.LocalizationService.Source("Text.RestartRequired770") : "");

        public ActionHistoryEntry(MaintenanceTask task, MaintenanceTaskResult result)
        {
            TaskId = task.Id; Name = task.Name;
            StartedAt = result.StartedAt; FinishedAt = result.FinishedAt; Duration = result.Duration;
            ExecutionStatus = result.ExecutionStatus; FindingStatus = result.FindingStatus;
            RequiresRestart = result.RequiresRestart; ExitCode = result.ExitCode;
            Summary = result.UserSummary; StdOut = result.StdOut ?? ""; StdErr = result.StdErr ?? "";
            var text = new StringBuilder();
            text.AppendLine(Summary);
            text.AppendLine(WinSereno.Localization.LocalizationService.Source("Text.Started") + StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"));
            text.AppendLine(WinSereno.Localization.LocalizationService.Source("Text.Finished") + FinishedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"));
            text.AppendLine(WinSereno.Localization.LocalizationService.Source("Text.Duration145") + DurationText);
            text.AppendLine(WinSereno.Localization.LocalizationService.Source("Text.Status") + StateText);
            text.AppendLine("ExecutionStatus: " + ExecutionStatus + " · FindingStatus: " + FindingStatus);
            text.AppendLine("ExitCode: " + (ExitCode?.ToString() ?? WinSereno.Localization.LocalizationService.Source("Text.Unavailable")));
            foreach (var step in result.SequenceSteps)
            {
                text.AppendLine();
                text.AppendLine(step.TaskId + WinSereno.Localization.LocalizationService.Source("Text.Separator") + step.Status);
                text.AppendLine(step.WasSkipped ? step.SkipReason : step.Result?.UserSummary);
                if (step.Result != null)
                    text.AppendLine("ExitCode: " + (step.Result.ExitCode?.ToString() ?? WinSereno.Localization.LocalizationService.Source("Text.Unavailable")) + WinSereno.Localization.LocalizationService.Source("Text.Duration") + step.Result.Duration);
            }
            if (result.CleanupBatch != null)
            {
                text.AppendLine(result.CleanupBatch.Totals.Summary);
                foreach (var step in result.CleanupBatch.Steps)
                    text.AppendLine(step.Category + WinSereno.Localization.LocalizationService.Source("Text.Separator") + (step.WasSkipped ? WinSereno.Localization.LocalizationService.Source("Text.Skipped748") + step.Reason : step.Result?.UserSummary));
            }
            details = text.ToString();
        }

        public TaskProgress CreateDetailsProgress() => new TaskProgress
        {
            CurrentTask = new MaintenanceTask { Id = TaskId, Name = Name },
            State = RunnerState.Completed, StartedAt = StartedAt, Elapsed = Duration,
            StdOut = WinSereno.Localization.LocalizationService.Current.Present(details) + "\nSTDOUT\n" + StdOut, StdErr = StdErr,
            Result = new MaintenanceTaskResult
            {
                StartedAt = StartedAt, FinishedAt = FinishedAt, Duration = Duration,
                ExecutionStatus = ExecutionStatus, FindingStatus = FindingStatus, RequiresRestart = RequiresRestart,
                ExitCode = ExitCode, UserSummary = Summary, StdOut = StdOut, StdErr = StdErr
            }
        };
    }
}
