using System;
using System.Collections.Generic;
using WinSereno.Models;
namespace WinSereno.Services
{
    public sealed class IntegritySessionState
    {
        private readonly Dictionary<string, MaintenanceTaskResult> results = new Dictionary<string, MaintenanceTaskResult>();
        public event EventHandler Changed;
        public MaintenanceTaskResult Get(string id) => results.TryGetValue(id, out var result) ? result : null;
        private static bool IsComponentStore(string id) => id == ElevatedTaskCatalog.CheckHealthId ||
            id == ElevatedTaskCatalog.ScanHealthId || id == ElevatedTaskCatalog.RestoreHealthId;
        public void Update(string id, MaintenanceTaskResult result)
        {
            if (IsComponentStore(id))
            {
                if (result == null || !result.CommandStarted || result.ExecutionStatus == ExecutionStatus.Cancelled ||
                    result.StartedAt == default(DateTimeOffset) || result.FinishedAt == default(DateTimeOffset) || result.FinishedAt < result.StartedAt) return;
                var previous = Get(id);
                if (previous != null && CompareTime(result, previous) < 0) return;
            }
            results[id] = result; Changed?.Invoke(this, EventArgs.Empty);
        }
        private static int CompareTime(MaintenanceTaskResult left, MaintenanceTaskResult right)
        {
            int end = left.FinishedAt.CompareTo(right.FinishedAt);
            return end != 0 ? end : left.StartedAt.CompareTo(right.StartedAt);
        }
        public static DiagnosticStatus Status(MaintenanceTaskResult r)
        {
            if (r == null) return DiagnosticStatus.NotChecked;
            if (r.FindingStatus == FindingStatus.RepairFailed || r.FindingStatus == FindingStatus.Unrepairable || r.FindingStatus == FindingStatus.SourceFilesNotFound) return DiagnosticStatus.Error;
            if (r.ExecutionStatus != ExecutionStatus.Success) return DiagnosticStatus.NotChecked;
            if (r.FindingStatus == FindingStatus.Healthy || r.FindingStatus == FindingStatus.Repaired) return DiagnosticStatus.Healthy;
            return r.FindingStatus == FindingStatus.RepairRequired || r.RequiresRestart ? DiagnosticStatus.Attention : DiagnosticStatus.NotChecked;
        }
        public DiagnosticResult Read()
        {
            string id = null;
            MaintenanceTaskResult result = null;
            foreach (var entry in results)
            {
                if (!IsComponentStore(entry.Key)) continue;
                int time = result == null ? 1 : CompareTime(entry.Value, result);
                // Identical timestamps: ordinal TaskId, independent of dictionary/insertion order.
                if (time > 0 || (time == 0 && StringComparer.Ordinal.Compare(entry.Key, id) < 0))
                { id = entry.Key; result = entry.Value; }
            }
            var files = Get(ElevatedTaskCatalog.SfcId);
            string details = result == null ? "Almacén de componentes: no comprobado." : "Almacén de componentes · " + id + " · " + result.FinishedAt.ToString("g") + " · " + result.Duration.TotalSeconds.ToString("0.0") + " s\n" + result.UserSummary + "\n" + result.StdOut + "\nSTDERR:\n" + result.StdErr;
            details += files == null ? "\nArchivos protegidos (SFC): no comprobado." : "\nArchivos protegidos (SFC), resultado independiente: " + files.UserSummary + "\n" + files.StdOut + "\nSTDERR:\n" + files.StdErr;
            return new DiagnosticResult { Id = "integrity", Name = "Integridad de Windows", Status = Status(result),
                Summary = result?.UserSummary ?? "La comprobación de integridad requiere una acción administrativa explícita.",
                DetailedDescription = details, Duration = result?.Duration ?? TimeSpan.Zero,
                Recommendation = "DISM informa sobre el almacén de componentes; SFC sobre archivos protegidos. Consulta los detalles diferenciados. El diagnóstico general no ejecuta estas herramientas.",
                NavigationTarget = NavigationSection.Repair, NavigationLabel = "Ir a Reparación" };
        }
    }
}
