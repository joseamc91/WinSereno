namespace WinSereno.Models
{
    public enum ImpactLevel { Information, Maintenance, Configuration, Repair }
    public enum TaskType { Internal, Command, PowerShell }
    public enum TaskCategory { Diagnosis, Repair, Network, Cleanup }
    public enum ExecutionStatus { Success, Failed, Cancelled, Unknown }
    public enum FindingStatus { Healthy, Attention, Repaired, RepairRequired, RepairFailed, Unrepairable, RestartRequired, Unknown, SourceFilesNotFound, ScanFailed, Completed, PartiallyCompleted, Failed }
    public enum RunnerState { Idle, Running, Cancelling, Completed }
    public enum NavigationSection { Home, Diagnosis, Repair, Network, Cleanup, Settings, Activity }
    public enum DiagnosticStatus { Healthy, Attention, Error, NotChecked }

    public sealed class MaintenanceTask
    {
        internal CleanupSelection CleanupSelection { get; set; }
        internal string ConfirmedThumbnailRoot { get; set; }
        internal string ConfirmedUserTempRoot { get; set; }
        internal RestartAdapter RestartAdapter { get; set; }
        internal DhcpRenewalPlan DhcpPlan { get; set; }
        public string ConfirmationWarning { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string ShortDescription { get; set; }
        public string DetailedDescription { get; set; }
        public TaskCategory Category { get; set; }
        public ImpactLevel ImpactLevel { get; set; }
        public TaskType TaskType { get; set; }
        public string Command { get; set; }
        public string Arguments { get; set; }
        public bool RequiresElevation { get; set; }
        public bool CanBeCancelled { get; set; }
        public bool MayRequireRestart { get; set; }
        public bool IsMock { get; set; }
    }
}
