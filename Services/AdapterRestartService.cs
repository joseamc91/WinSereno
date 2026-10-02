using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using WinSereno.Models;
namespace WinSereno.Services
{
    public sealed class AdapterRestartService
    {
        private readonly ISessionLogger logger;
        public AdapterRestartService(ISessionLogger logger) { this.logger = logger; }
        internal Action<string> DiagnosticMessage { get; set; }
        private void Log(string text) { if (logger != null) SystemQuery.Log(logger, text); DiagnosticMessage?.Invoke(text); }
        private static IList<Dictionary<string, object>> ReadPhysical() => SystemQuery.Read(
            "SELECT GUID, NetConnectionID, Name, NetConnectionStatus, NetEnabled, ServiceName FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE",
            "GUID", "NetConnectionID", "Name", "NetConnectionStatus", "NetEnabled", "ServiceName");
        public AdapterRestartSelection ReadSelection()
        {
            var selected = new List<RestartAdapter>();
            try
            {
                var physical = ReadPhysical(); var all = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in all)
                {
                    if (!Guid.TryParse(nic.Id, out Guid id)) continue;
                    var rows = physical.Where(r => Guid.TryParse(r["GUID"] as string, out Guid other) && other == id).ToArray();
                    string kind = nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "Ethernet" : nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Otro";
                    bool known = rows.Length == 1 && string.Equals(rows[0]["NetConnectionID"] as string, nic.Name, StringComparison.Ordinal);
                    bool active = known && nic.OperationalStatus == OperationalStatus.Up && rows[0]["NetConnectionStatus"] != null && Convert.ToInt32(rows[0]["NetConnectionStatus"]) == 2;
                    string driver = known ? rows[0]["ServiceName"] as string : null;
                    bool unique = all.Count(n => string.Equals(n.Name, nic.Name, StringComparison.OrdinalIgnoreCase)) == 1;
                    if (!unique || !AdapterRestartPolicy.IsEligible(known, active, kind, nic.Name, nic.Description, driver))
                    { Log("Selección reinicio excluida: " + nic.Name + " | Físico=" + known + " | Activo=" + active + " | Tipo=" + kind); continue; }
                    selected.Add(new RestartAdapter(id.ToString("D"), nic.Name, nic.Description, kind));
                    Log("Selección reinicio elegible: " + nic.Name + " | " + kind + " | " + nic.Description);
                }
                return new AdapterRestartSelection(selected.OrderBy(a => a.InterfaceId, StringComparer.Ordinal).ToArray(), true);
            }
            catch (Exception ex) { Log("No se pudo verificar selección reinicio: " + ex); return new AdapterRestartSelection(new RestartAdapter[0], false); }
        }
        public static string Fingerprint(RestartAdapter adapter)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(adapter.InterfaceId + "\t" + adapter.Name + "\t" + adapter.Kind))).Replace("-", "");
        }
        internal RestartAdapter ResolveConfirmed(string fingerprint)
        {
            if (fingerprint == null || fingerprint.Length != 64) throw new InvalidOperationException("Identidad de selección inválida.");
            var selection = ReadSelection();
            var matches = selection.Adapters.Where(a => Fingerprint(a) == fingerprint).ToArray();
            if (!selection.ReadSucceeded || matches.Length != 1) throw new InvalidOperationException("El adaptador seleccionado ya no es físico, activo o tiene una identidad distinta. No se ejecuta nada.");
            return matches[0];
        }
        public static MaintenanceTask PrepareTask(RestartAdapter adapter)
        {
            var task = ElevatedTaskCatalog.Get(ElevatedTaskCatalog.RestartAdapterId); task.RestartAdapter = adapter;
            task.DetailedDescription = "Adaptador seleccionado: " + adapter.DisplayName + "\n\n" + task.DetailedDescription;
            task.Command = Command(adapter, 0).Command + " " + Command(adapter, 0).Arguments + "\n" + Command(adapter, 1).Command + " " + Command(adapter, 1).Arguments;
            task.Arguments = "Selección interna confirmada; disable y después enable. Hasta tres intentos de enable si falla la recuperación.";
            return task;
        }
        internal static MaintenanceTask Command(RestartAdapter adapter, int attempt)
        {
            if (adapter == null || !DhcpRenewalPolicy.IsSafeName(adapter.Name) || !Guid.TryParse(adapter.InterfaceId, out Guid unused) || attempt < 0 || attempt > AdapterRestartPolicy.EnableAttempts)
                throw new InvalidOperationException("Selección o intento inválido.");
            return new MaintenanceTask { Id = ElevatedTaskCatalog.RestartAdapterId + (attempt == 0 ? "/disable" : "/enable." + attempt),
                Name = adapter.Name + (attempt == 0 ? " · Deshabilitar" : " · Habilitar, intento " + attempt), Category = TaskCategory.Network,
                ImpactLevel = ImpactLevel.Configuration, TaskType = TaskType.Command, RequiresElevation = true, CanBeCancelled = false,
                Command = Path.Combine(Path.GetDirectoryName(ElevatedTaskCatalog.Get(ElevatedTaskCatalog.FlushDnsId).Command), "netsh.exe"),
                Arguments = "interface set interface name=\"" + adapter.Name + "\" admin=" + (attempt == 0 ? "disabled" : "enabled") };
        }
        internal void ValidateTarget(RestartAdapter adapter, int attempt)
        {
            if (attempt == 0) { ResolveConfirmed(Fingerprint(adapter)); return; }
            // Disabled interfaces may disappear from .NET's active list. Revalidate physical identity in WMI, never by an arbitrary UI name.
            FindPhysical(adapter);
        }
        private static Dictionary<string, object> FindPhysical(RestartAdapter adapter)
        {
            var physical = ReadPhysical();
            var rows = physical.Where(r => Guid.TryParse(r["GUID"] as string, out Guid id) && id.ToString("D") == adapter.InterfaceId).ToArray();
            if (rows.Length != 1 || (rows[0]["NetConnectionID"] as string) != adapter.Name ||
                physical.Count(r => string.Equals(r["NetConnectionID"] as string, adapter.Name, StringComparison.OrdinalIgnoreCase)) != 1 ||
                !AdapterRestartPolicy.IsEligible(true, true, adapter.Kind, adapter.Name, rows[0]["Name"] as string, rows[0]["ServiceName"] as string))
                throw new InvalidOperationException("La identidad física del adaptador cambió. No se habilita otra interfaz con el mismo nombre.");
            return rows[0];
        }
        internal AdapterRestartState ReadState(RestartAdapter adapter)
        {
            try
            {
                var row = FindPhysical(adapter);
                if (row["NetEnabled"] is bool enabled && !enabled) return AdapterRestartState.Disabled;
                if (row["NetConnectionStatus"] != null && Convert.ToInt32(row["NetConnectionStatus"]) == 4) return AdapterRestartState.Disabled;
                if (!(row["NetEnabled"] is bool isEnabled) || !isEnabled) return AdapterRestartState.Unknown;
                var nic = NetworkInterface.GetAllNetworkInterfaces().SingleOrDefault(n => Guid.TryParse(n.Id, out Guid id) && id.ToString("D") == adapter.InterfaceId && n.Name == adapter.Name);
                return nic != null && nic.OperationalStatus == OperationalStatus.Up ? AdapterRestartState.Active : AdapterRestartState.Enabled;
            }
            catch (Exception ex) { Log("No se pudo verificar estado de " + adapter.Name + ": " + ex); return AdapterRestartState.Unknown; }
        }
        internal async Task<AdapterRestartState> WaitStateAsync(RestartAdapter adapter, bool disabled)
        {
            var watch = Stopwatch.StartNew(); AdapterRestartState state;
            do
            {
                state = ReadState(adapter);
                if (disabled ? state == AdapterRestartState.Disabled : state == AdapterRestartState.Active) return state;
                if (watch.Elapsed.TotalSeconds >= (disabled ? AdapterRestartPolicy.DisabledWaitSeconds : AdapterRestartPolicy.ActiveWaitSeconds)) break;
                await Task.Delay(500).ConfigureAwait(false);
            } while (true);
            return state;
        }
    }
}
