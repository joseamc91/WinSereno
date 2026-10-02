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
                if (ExecutionStatus == ExecutionStatus.Cancelled) return "Cancelada";
                if (ExecutionStatus == ExecutionStatus.Failed) return "Fallo";
                switch (FindingStatus)
                {
                    case FindingStatus.Healthy: return "Correcto";
                    case FindingStatus.Repaired: return "Reparado";
                    case FindingStatus.Completed: return "Completado";
                    case FindingStatus.PartiallyCompleted: return "Parcial";
                    case FindingStatus.Attention:
                    case FindingStatus.RepairRequired: return "Atención";
                    case FindingStatus.RestartRequired: return "Reinicio requerido";
                    case FindingStatus.RepairFailed:
                    case FindingStatus.Unrepairable:
                    case FindingStatus.SourceFilesNotFound:
                    case FindingStatus.ScanFailed:
                    case FindingStatus.Failed: return "Fallo";
                    default: return "No comprobado";
                }
            }
        }
        public string StateText => StatusText + (RequiresRestart && FindingStatus != FindingStatus.RestartRequired ? " · Reinicio requerido" : "");

        public ActionHistoryEntry(MaintenanceTask task, MaintenanceTaskResult result)
        {
            TaskId = task.Id; Name = task.Name;
            StartedAt = result.StartedAt; FinishedAt = result.FinishedAt; Duration = result.Duration;
            ExecutionStatus = result.ExecutionStatus; FindingStatus = result.FindingStatus;
            RequiresRestart = result.RequiresRestart; ExitCode = result.ExitCode;
            Summary = result.UserSummary; StdOut = result.StdOut ?? ""; StdErr = result.StdErr ?? "";
            var text = new StringBuilder();
            text.AppendLine(Summary);
            text.AppendLine("Inicio: " + StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"));
            text.AppendLine("Finalización: " + FinishedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"));
            text.AppendLine("Duración: " + DurationText);
            text.AppendLine("Estado: " + StateText);
            text.AppendLine("ExecutionStatus: " + ExecutionStatus + " · FindingStatus: " + FindingStatus);
            text.AppendLine("ExitCode: " + (ExitCode?.ToString() ?? "No disponible"));
            foreach (var step in result.SequenceSteps)
            {
                text.AppendLine();
                text.AppendLine(step.TaskId + " · " + step.Status);
                text.AppendLine(step.WasSkipped ? step.SkipReason : step.Result?.UserSummary);
                if (step.Result != null)
                    text.AppendLine("ExitCode: " + (step.Result.ExitCode?.ToString() ?? "No disponible") + " · Duración: " + step.Result.Duration);
            }
            if (result.CleanupBatch != null)
            {
                text.AppendLine(result.CleanupBatch.Totals.Summary);
                foreach (var step in result.CleanupBatch.Steps)
                    text.AppendLine(step.Category + " · " + (step.WasSkipped ? "Omitido: " + step.Reason : step.Result?.UserSummary));
            }
            details = text.ToString();
        }

        public TaskProgress CreateDetailsProgress() => new TaskProgress
        {
            CurrentTask = new MaintenanceTask { Id = TaskId, Name = Name },
            State = RunnerState.Completed, StartedAt = StartedAt, Elapsed = Duration,
            StdOut = details + "\nSTDOUT\n" + StdOut, StdErr = StdErr,
            Result = new MaintenanceTaskResult
            {
                StartedAt = StartedAt, FinishedAt = FinishedAt, Duration = Duration,
                ExecutionStatus = ExecutionStatus, FindingStatus = FindingStatus, RequiresRestart = RequiresRestart,
                ExitCode = ExitCode, UserSummary = Summary, StdOut = StdOut, StdErr = StdErr
            }
        };
    }
}
