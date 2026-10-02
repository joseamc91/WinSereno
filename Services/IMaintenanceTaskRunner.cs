using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WinSereno.Models;

namespace WinSereno.Services
{
    public interface ICleanupAnalysisRunner
    {
        Task<MaintenanceTaskResult> RunCleanupAnalysisAsync(OperationLease lease, Action<TaskProgress> progress);
    }
    public interface IMaintenanceTaskRunner
    {
        bool IsActive { get; }
        TaskProgress Current { get; }
        event EventHandler<TaskProgress> ProgressChanged;
        Task<MaintenanceTaskResult> RunAsync(MaintenanceTask task);
        bool RequestCancellation();
        Task WaitForIdleAsync();
    }
    // Contract only. A future implementation must use the same application-wide runner
    // and evaluate selection, policies and ContinueOnFailure between sequential steps.
    public interface IMaintenanceSequenceRunner
    {
        Task<IReadOnlyList<SequenceStepResult>> RunAsync(MaintenanceSequence sequence);
    }
}
