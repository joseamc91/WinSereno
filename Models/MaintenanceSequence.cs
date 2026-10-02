using System.Collections.Generic;

namespace WinSereno.Models
{
    public enum SequenceDecision { Run, Skip, Stop }
    public sealed class MaintenanceSequence
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public IList<MaintenanceSequenceStep> Steps { get; } = new List<MaintenanceSequenceStep>();
    }
    public sealed class MaintenanceSequenceStep
    {
        public MaintenanceTask Task { get; set; }
        public bool IsSelected { get; set; } = true;
        public bool ContinueOnFailure { get; set; } = true;
        public ISequenceStepPolicy Policy { get; set; }
    }
    public sealed class SequenceStepResult
    {
        public string Status => WasSkipped ? "Skipped" : Result?.ExecutionStatus.ToString() ?? "Unknown";
        public string TaskId { get; set; }
        public bool WasSkipped { get; set; }
        public string SkipReason { get; set; }
        public MaintenanceTaskResult Result { get; set; }
    }
    public interface ISequenceStepPolicy
    {
        SequenceDecision Decide(MaintenanceSequenceStep step, IReadOnlyList<SequenceStepResult> previousResults);
    }
}
