using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WinSereno.Models;
namespace WinSereno.Services
{
    public static class DhcpRenewalPolicy
    {
        public static bool IsSafeName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 256 && !name.StartsWith("-", StringComparison.Ordinal) &&
            !name.Any(c => char.IsControl(c) || c == '"' || c == '*' || c == '?' || c == '/' || c == '\\');
        public static bool IsEligible(bool physical, bool active, bool dhcp, bool ipv4, string kind, string name, string description, string driverService = null)
        {
            if (string.Equals(driverService, "BthPan", StringComparison.OrdinalIgnoreCase)) return false;
            if (!physical || !active || !dhcp || !ipv4 || (kind != "Ethernet" && kind != "Wi-Fi") || !IsSafeName(name)) return false;
            var text = (description ?? "") + " " + name;
            return !new[] { "virtual", "hyper-v", "vmware", "virtualbox", "vpn", "tunnel", "tap-", "wireguard", "wintun", "bluetooth", "loopback" }
                .Any(marker => text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        private static bool ReportsFailure(MaintenanceTaskResult result) => !string.IsNullOrWhiteSpace(result.StdErr) || Regex.IsMatch(result.StdOut ?? "",
            @"(?im)^\s*(?:An error occurred while (?:releasing|renewing) interface|Se produjo un error al (?:liberar|renovar) la interfaz|No operation can be performed|No se puede realizar ninguna operación|The requested operation requires elevation|La operación solicitada requiere|Access is denied|Acceso denegado)");
        public static bool NeedsElevation(MaintenanceTaskResult result) => result.ExecutionStatus == ExecutionStatus.Failed &&
            (result.ExitCode == 5 || Regex.IsMatch((result.StdOut ?? "") + "\n" + (result.StdErr ?? ""),
                @"(?im)(?:The requested operation requires elevation\.|La operación solicitada requiere (?:ejecución con )?elevación\.|Access is denied\.|Acceso denegado\.)"));
        public static bool ShouldRenew(MaintenanceTaskResult release) => release.ExecutionStatus == ExecutionStatus.Success && release.FindingStatus == FindingStatus.Completed;
        public static void ApplyCommand(bool release, MaintenanceTaskResult result, DhcpVerification verification)
        {
            result.RequiresRestart = false; result.FindingStatus = FindingStatus.Unknown;
            if (!result.ExitCode.HasValue) { result.ExecutionStatus = ExecutionStatus.Failed; result.UserSummary = "No se pudo iniciar o confirmar el comando DHCP."; return; }
            result.ExecutionStatus = result.ExitCode == 0 ? ExecutionStatus.Success : ExecutionStatus.Failed;
            if (result.ExitCode != 0) { result.UserSummary = "El comando " + (release ? "release" : "renew") + " falló; consulta su salida original."; return; }
            if (ReportsFailure(result)) { result.ExecutionStatus = ExecutionStatus.Failed; result.UserSummary = "Windows informó de un fallo en " + (release ? "release" : "renew") + " aunque devolvió ExitCode 0. Consulta su salida."; return; }
            bool confirmed = release ? verification == DhcpVerification.Released : verification == DhcpVerification.Renewed;
            if (confirmed) result.FindingStatus = FindingStatus.Completed;
            result.UserSummary = confirmed ? release ? "Se confirmó la liberación de IPv4 en esta interfaz." : "Windows tiene una dirección IPv4 DHCP válida en esta interfaz; esto no comprueba Internet." :
                "ExitCode 0, pero no se pudo confirmar " + (release ? "la liberación de IPv4." : "una dirección IPv4 DHCP válida.");
        }
        public static async Task<DhcpRunOutcome> RunAsync(int adapterCount, int startOrdinal, Func<int, Task<MaintenanceTaskResult>> execute,
            Func<int, string> stepId, Action<SequenceStepResult> skipped, bool pauseForElevation, Func<bool> canStartAdapter = null)
        {
            var outcome = new DhcpRunOutcome();
            for (int ordinal = startOrdinal; ordinal < adapterCount * 2; ordinal++)
            {
                if (ordinal % 2 == 0 && canStartAdapter != null && !canStartAdapter())
                {
                    for (int rest = ordinal; rest < adapterCount * 2; rest++) Skip(outcome, stepId(rest), "Comunicación IPC interrumpida; no se actúa sobre nuevas interfaces.", skipped);
                    break;
                }
                var result = await execute(ordinal);
                outcome.Steps.Add(new SequenceStepResult { TaskId = stepId(ordinal), Result = result });
                if (pauseForElevation && NeedsElevation(result))
                { outcome.ElevationResumeOrdinal = ordinal; return outcome; }
                if (ordinal % 2 == 0 && !ShouldRenew(result))
                {
                    ordinal++;
                    Skip(outcome, stepId(ordinal), "No se ejecutó renew porque release falló o su resultado no pudo confirmarse.", skipped);
                }
            }
            return outcome;
        }
        private static void Skip(DhcpRunOutcome outcome, string id, string reason, Action<SequenceStepResult> notify)
        { var step = new SequenceStepResult { TaskId = id, WasSkipped = true, SkipReason = reason }; outcome.Steps.Add(step); notify(step); }
        public static void Summarize(MaintenanceTaskResult result, IEnumerable<string> requiredIds)
        {
            if (result.ExecutionStatus == ExecutionStatus.Cancelled) { result.FindingStatus = FindingStatus.Unknown; result.UserSummary = "Elevación cancelada; no se completó la renovación DHCP. Consulta los pasos ejecutados y omitidos."; return; }
            var last = requiredIds.Select(id => result.SequenceSteps.LastOrDefault(s => s.TaskId == id)).ToArray();
            bool failed = last.Any(s => s == null || s.WasSkipped || s.Result.ExecutionStatus == ExecutionStatus.Failed);
            bool confirmed = last.Length > 0 && last.All(s => s != null && !s.WasSkipped && s.Result.ExecutionStatus == ExecutionStatus.Success && s.Result.FindingStatus == FindingStatus.Completed);
            result.ExecutionStatus = failed ? ExecutionStatus.Failed : ExecutionStatus.Success;
            result.FindingStatus = confirmed ? FindingStatus.Completed : FindingStatus.Unknown;
            result.UserSummary = confirmed ? "Se completó la renovación DHCP de las interfaces confirmadas. La conectividad se comprueba por separado." :
                failed ? "La renovación DHCP no se completó en todas las interfaces. Consulta qué comando falló y qué pasos se omitieron." : "Los comandos terminaron, pero no se pudo confirmar completamente la renovación DHCP.";
            result.ExitCode = null; result.RequiresRestart = false;
        }
    }
}
