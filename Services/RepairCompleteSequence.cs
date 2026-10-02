using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WinSereno.Models;

namespace WinSereno.Services
{
    // Only this fixed policy is exposed by the elevated catalog. Delegates allow process-free tests.
    public static class RepairCompleteSequence
    {
        public static IReadOnlyList<string> TaskIds { get; } = Array.AsReadOnly(new[] { ElevatedTaskCatalog.ScanHealthId, ElevatedTaskCatalog.RestoreHealthId, ElevatedTaskCatalog.SfcId });
        public static async Task<IReadOnlyList<SequenceStepResult>> RunAsync(Func<string, Task<MaintenanceTaskResult>> execute, Action<SequenceStepResult> skipped)
        {
            var steps = new List<SequenceStepResult>();
            string stopReason = null;
            for (int i = 0; i < TaskIds.Count; i++)
            {
                string id = TaskIds[i];
                if (stopReason != null || (i == 1 && steps[0].Result.FindingStatus == FindingStatus.Healthy))
                {
                    var skip = new SequenceStepResult { TaskId = id, WasSkipped = true,
                        SkipReason = stopReason ?? "ScanHealth = Healthy: no se necesita RestoreHealth." };
                    steps.Add(skip); skipped(skip); continue;
                }
                MaintenanceTaskResult result;
                try { result = await execute(id).ConfigureAwait(false); }
                catch (Exception ex) { result = new MaintenanceTaskResult { ExecutionStatus = ExecutionStatus.Failed, UserSummary = "No se pudo ejecutar el paso: " + ex.GetType().Name }; }
                steps.Add(new SequenceStepResult { TaskId = id, Result = result });
                bool accepted = result.ExecutionStatus == ExecutionStatus.Success &&
                    (i == 0 ? result.FindingStatus == FindingStatus.Healthy || result.FindingStatus == FindingStatus.RepairRequired :
                    result.FindingStatus == FindingStatus.Healthy || result.FindingStatus == FindingStatus.Repaired);
                if (!accepted) stopReason = "Detenida en " + ElevatedTaskCatalog.Get(id).Name + " (" + result.ExecutionStatus + "/" + result.FindingStatus + "): " + result.UserSummary;
            }
            return steps;
        }
        public static void MarkUnexecuted(MaintenanceTaskResult result, string reason)
        {
            foreach (string id in TaskIds)
                if (!result.SequenceSteps.Any(s => s.TaskId == id))
                    result.SequenceSteps.Add(new SequenceStepResult { TaskId = id, WasSkipped = true, SkipReason = reason });
        }
        public static void Summarize(MaintenanceTaskResult result)
        {
            var executed = result.SequenceSteps.Where(s => !s.WasSkipped).ToList();
            var last = executed.LastOrDefault()?.Result;
            bool finished = executed.LastOrDefault()?.TaskId == ElevatedTaskCatalog.SfcId && last.ExecutionStatus == ExecutionStatus.Success &&
                (last.FindingStatus == FindingStatus.Healthy || last.FindingStatus == FindingStatus.Repaired);
            result.ExecutionStatus = finished ? ExecutionStatus.Success : last?.ExecutionStatus == ExecutionStatus.Cancelled || result.ExecutionStatus == ExecutionStatus.Cancelled ? ExecutionStatus.Cancelled : ExecutionStatus.Failed;
            result.FindingStatus = finished ? executed.Any(s => s.Result.FindingStatus == FindingStatus.Repaired) ? FindingStatus.Repaired : FindingStatus.Healthy : last?.FindingStatus ?? FindingStatus.Unknown;
            result.RequiresRestart = executed.Any(s => s.Result.RequiresRestart);
            result.ExitCode = null; // A sequence has no single process exit code; retain every substep's code.
            result.UserSummary = finished ? "Reparación completa finalizada. " + last.UserSummary :
                executed.Count == 0 ? result.ExecutionStatus == ExecutionStatus.Cancelled ? "Secuencia cancelada por el usuario antes de iniciar los pasos." : "No se pudo iniciar la secuencia." :
                "Secuencia detenida en " + ElevatedTaskCatalog.Get(executed.Last().TaskId).Name + ": " + last.UserSummary;
            foreach (var skip in result.SequenceSteps.Where(s => s.WasSkipped))
                result.UserSummary += "\nOmitido " + ElevatedTaskCatalog.Get(skip.TaskId).Name + ": " + skip.SkipReason;
            if (result.RequiresRestart) result.UserSummary += "\nEs necesario reiniciar Windows; no se reinicia automáticamente.";
        }
    }
}
