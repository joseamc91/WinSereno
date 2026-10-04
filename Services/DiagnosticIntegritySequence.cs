using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinSereno.Models;

namespace WinSereno.Services
{
    public static class DiagnosticIntegritySequence
    {
        public static IReadOnlyList<string> TaskIds { get; } = Array.AsReadOnly(new[] { ElevatedTaskCatalog.CheckHealthId, ElevatedTaskCatalog.SfcVerifyOnlyId });
        public static async Task RunAsync(Func<string, Task<MaintenanceTaskResult>> execute)
        {
            // Both checks are independent and strictly read-only. A failed check does not suppress the other one.
            foreach (string id in TaskIds) await execute(id).ConfigureAwait(false);
        }
        public static void CompleteMissing(MaintenanceTaskResult result)
        {
            foreach (string id in TaskIds)
                if (!result.SequenceSteps.Any(s => s.TaskId == id))
                    result.SequenceSteps.Add(new SequenceStepResult { TaskId = id, WasSkipped = true,
                        SkipReason = result.ExecutionStatus == ExecutionStatus.Cancelled ? "Permisos cancelados; no se inició la comprobación." : "No se pudo confirmar la ejecución del paso." });
        }
        private static DiagnosticStatus Status(SequenceStepResult step)
        {
            var r = step?.Result;
            if (step == null || step.WasSkipped || r == null || !r.CommandStarted || r.ExecutionStatus != ExecutionStatus.Success) return DiagnosticStatus.NotChecked;
            if (r.FindingStatus == FindingStatus.Unrepairable) return DiagnosticStatus.Error;
            if (r.FindingStatus == FindingStatus.RepairRequired || r.FindingStatus == FindingStatus.Attention) return DiagnosticStatus.Attention;
            return r.FindingStatus == FindingStatus.Healthy ? DiagnosticStatus.Healthy : DiagnosticStatus.NotChecked;
        }
        public static DiagnosticResult ToDiagnostic(MaintenanceTaskResult result)
        {
            var store = result.SequenceSteps.FirstOrDefault(s => s.TaskId == ElevatedTaskCatalog.CheckHealthId);
            var files = result.SequenceSteps.FirstOrDefault(s => s.TaskId == ElevatedTaskCatalog.SfcVerifyOnlyId);
            var a = Status(store); var b = Status(files);
            var status = a == DiagnosticStatus.Error ? DiagnosticStatus.Error : a == DiagnosticStatus.Attention || b == DiagnosticStatus.Attention ? DiagnosticStatus.Attention :
                a == DiagnosticStatus.Healthy && b == DiagnosticStatus.Healthy ? DiagnosticStatus.Healthy : DiagnosticStatus.NotChecked;
            bool cancelled = result.ExecutionStatus == ExecutionStatus.Cancelled && result.SequenceSteps.All(s => s.WasSkipped || s.Result?.CommandStarted != true);
            var details = new StringBuilder();
            var nativeRanges = new List<NativeTextRange>();
            Describe(details, "Almacén de componentes", store, a, nativeRanges);
            Describe(details, "Archivos protegidos", files, b, nativeRanges);
            if (result.SequenceSteps.All(s => s.WasSkipped)) details.AppendLine(result.UserSummary);
            return new DiagnosticResult { Id = "integrity", Name = "Integridad de Windows", Status = status, Duration = result.Duration,
                Summary = cancelled ? "No se comprobó la integridad porque se cancelaron los permisos de administrador." :
                    CombinedSummary(a, b),
                DetailedDescription = details.ToString(),
                NativeOutputSegments = result.SequenceSteps.Where(s => s.Result != null).SelectMany(s => new[] { s.Result.StdOut, s.Result.StdErr }).Where(s => !string.IsNullOrEmpty(s)).ToList(),
                NativeOutputRanges = nativeRanges,
                Recommendation = status == DiagnosticStatus.Attention || status == DiagnosticStatus.Error ? "Revisar las opciones de Reparación; ninguna se ha ejecutado automáticamente." : null,
                NavigationTarget = status == DiagnosticStatus.Attention || status == DiagnosticStatus.Error ? (NavigationSection?)NavigationSection.Repair : null,
                NavigationLabel = status == DiagnosticStatus.Attention || status == DiagnosticStatus.Error ? "Ir a Reparación" : null };
        }
        private static string CombinedSummary(DiagnosticStatus store, DiagnosticStatus files)
        {
            if (store == DiagnosticStatus.Healthy && files == DiagnosticStatus.Healthy)
                return "No se detectaron problemas de integridad en Windows.";
            if (store == DiagnosticStatus.NotChecked && files == DiagnosticStatus.NotChecked)
                return "No se pudieron comprobar completamente el almacén de componentes ni los archivos protegidos. No se realizó ninguna reparación.";
            bool storeProblem = store == DiagnosticStatus.Attention || store == DiagnosticStatus.Error;
            bool filesProblem = files == DiagnosticStatus.Attention || files == DiagnosticStatus.Error;
            if (storeProblem && filesProblem)
                return "Se detectaron problemas tanto en el almacén de componentes como en los archivos protegidos. No se realizó ninguna reparación.";
            string storeText = store == DiagnosticStatus.Healthy ? "El almacén de componentes no presenta corrupción" :
                store == DiagnosticStatus.Attention ? "El almacén de componentes necesita reparación" :
                store == DiagnosticStatus.Error ? "El almacén de componentes presenta corrupción no reparable" :
                "No se pudo comprobar completamente el almacén de componentes";
            string filesText = files == DiagnosticStatus.Healthy ? "los archivos protegidos no presentan infracciones" :
                filesProblem ? "se detectaron infracciones en archivos protegidos" :
                "no se pudieron comprobar completamente los archivos protegidos";
            return storeText + "; " + filesText + ". No se realizó ninguna reparación.";
        }
        private static void Describe(StringBuilder text, string name, SequenceStepResult step, DiagnosticStatus status, IList<NativeTextRange> nativeRanges)
        {
            text.AppendLine(name + " · " + new DiagnosticResult { Status = status }.StatusLabel);
            if (step?.Result == null || step.WasSkipped) { text.AppendLine(step?.SkipReason ?? "No comprobado."); return; }
            var r = step.Result;
            text.AppendLine("Duración: " + r.Duration.TotalSeconds.ToString("0.0") + " s · ExitCode: " + (r.ExitCode?.ToString() ?? "no disponible"));
            text.AppendLine(r.UserSummary);
            if (!string.IsNullOrEmpty(r.StdOut)) nativeRanges.Add(new NativeTextRange(text.Length, r.StdOut.Length));
            text.AppendLine(r.StdOut); text.AppendLine("STDERR:");
            if (!string.IsNullOrEmpty(r.StdErr)) nativeRanges.Add(new NativeTextRange(text.Length, r.StdErr.Length));
            text.AppendLine(r.StdErr);
        }
        public static void Summarize(MaintenanceTaskResult result)
        {
            var card = ToDiagnostic(result);
            result.ExitCode = null;
            result.FindingStatus = card.Status == DiagnosticStatus.Healthy ? FindingStatus.Healthy : card.Status == DiagnosticStatus.Attention ? FindingStatus.Attention :
                card.Status == DiagnosticStatus.Error ? FindingStatus.Unrepairable : FindingStatus.Unknown;
            result.UserSummary = card.Summary;
            if (result.ExecutionStatus != ExecutionStatus.Cancelled)
                result.ExecutionStatus = card.Status == DiagnosticStatus.NotChecked ? ExecutionStatus.Failed : ExecutionStatus.Success;
        }
    }
}
