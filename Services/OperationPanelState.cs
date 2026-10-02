using WinSereno.Models;

namespace WinSereno.Services
{
    public sealed class OperationPanelState
    {
        private MaintenanceTaskResult lastResult;
        public bool IsDismissed { get; private set; }
        public void Observe(TaskProgress progress)
        {
            if (progress?.State == RunnerState.Running || progress?.State == RunnerState.Cancelling) IsDismissed = false;
            if (progress?.State == RunnerState.Completed && progress.Result != null && !ReferenceEquals(lastResult, progress.Result))
            {
                lastResult = progress.Result; IsDismissed = false;
            }
        }
        public bool CanDismiss(TaskProgress progress, bool operationActive)
            => !operationActive && progress?.CurrentTask != null && progress.State == RunnerState.Completed && progress.Result != null;
        public void Dismiss(TaskProgress progress, bool operationActive)
        {
            if (CanDismiss(progress, operationActive)) IsDismissed = true;
        }
    }
}
