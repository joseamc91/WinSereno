using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using WinSereno.Models;
namespace WinSereno.Services
{
    public sealed class DhcpRenewalService
    {
        private readonly ISessionLogger logger;
        public DhcpRenewalService(ISessionLogger logger) { this.logger = logger; }
        private void Log(string text) { if (logger != null) SystemQuery.Log(logger, text); }
        public DhcpRenewalPlan ReadPlan()
        {
            var selected = new List<DhcpAdapter>(); var exclusions = new List<string>(); bool succeeded = true;
            try
            {
                var physical = SystemQuery.Read("SELECT GUID, NetConnectionID, NetConnectionStatus, ServiceName FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE", "GUID", "NetConnectionID", "NetConnectionStatus", "ServiceName");
                var configs = SystemQuery.Read("SELECT SettingID, DHCPEnabled FROM Win32_NetworkAdapterConfiguration", "SettingID", "DHCPEnabled");
                var all = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in all)
                {
                    if (nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet && nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) continue;
                    try
                    {
                        if (!Guid.TryParse(nic.Id, out Guid id)) continue;
                        var rows = physical.Where(r => Guid.TryParse(r["GUID"] as string, out Guid other) && other == id).ToArray();
                        var configuration = configs.Where(r => Guid.TryParse(r["SettingID"] as string, out Guid other) && other == id).ToArray();
                        bool knownPhysical = rows.Length == 1 && string.Equals(rows[0]["NetConnectionID"] as string, nic.Name, StringComparison.OrdinalIgnoreCase);
                        bool active = nic.OperationalStatus == OperationalStatus.Up && knownPhysical && rows[0]["NetConnectionStatus"] != null && Convert.ToInt32(rows[0]["NetConnectionStatus"]) == 2;
                        bool ipv4 = nic.Supports(NetworkInterfaceComponent.IPv4);
                        bool dhcp = ipv4 && nic.GetIPProperties().GetIPv4Properties().IsDhcpEnabled && configuration.Length == 1 && configuration[0]["DHCPEnabled"] is bool enabled && enabled;
                        var kind = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Ethernet";
                        string driverService = rows.Length == 1 ? rows[0]["ServiceName"] as string : null;
                        bool uniqueName = all.Count(n => string.Equals(n.Name, nic.Name, StringComparison.OrdinalIgnoreCase)) == 1;
                        if (!uniqueName || !DhcpRenewalPolicy.IsEligible(knownPhysical, active, dhcp, ipv4, kind, nic.Name, nic.Description, driverService))
                        {
                            string reason = string.Equals(driverService, "BthPan", StringComparison.OrdinalIgnoreCase) ? "Bluetooth PAN excluido" : !knownPhysical ? "no físico confirmado" : !active ? "desconectado/no activo" : !dhcp ? "IPv4 sin DHCP confirmado" : "nombre o tipo excluido por seguridad";
                            exclusions.Add(nic.Name + ": " + reason); Log("Selección DHCP excluida: " + exclusions.Last()); continue;
                        }
                        var addresses = nic.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString());
                        selected.Add(new DhcpAdapter(id.ToString("D"), nic.Name, nic.Description, kind, string.Join(", ", addresses)));
                        Log("Selección DHCP elegible: " + nic.Name + " | " + kind + " | DHCP=True | IPv4=" + selected.Last().IPv4);
                    }
                    catch (Exception ex) { succeeded = false; Log("Error al verificar interfaz DHCP: " + ex); }
                }
            }
            catch (Exception ex) { succeeded = false; Log("No se pudo completar selección DHCP: " + ex); }
            if (!succeeded) selected.Clear();
            return new DhcpRenewalPlan(selected.OrderBy(a => a.InterfaceId, StringComparer.Ordinal).ToArray(), exclusions, succeeded);
        }
        public static string Fingerprint(DhcpRenewalPlan plan)
        {
            var text = string.Join("\n", plan.Adapters.Select(a => a.InterfaceId + "\t" + a.Name + "\t" + a.Kind));
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }
        public static MaintenanceTask PrepareTask(DhcpRenewalPlan plan)
        {
            if (!plan.ReadSucceeded || plan.Adapters.Count == 0) throw new InvalidOperationException("No hay selección DHCP segura.");
            var task = ElevatedTaskCatalog.Get(ElevatedTaskCatalog.RenewDhcpId); task.DhcpPlan = plan;
            task.DetailedDescription = "Adaptadores que se renovarán:\n" + string.Join("\n", plan.Adapters.Select(a => a.Name + " · " + a.Kind + " · " + a.Description + " · IPv4 DHCP: " + a.IPv4)) + "\n\n" + task.DetailedDescription;
            task.Command = string.Join("\n", Enumerable.Range(0, plan.Adapters.Count * 2).Select(o => { var command = Command(plan, o); return command.Command + " " + command.Arguments; }));
            task.Arguments = "Selección interna confirmada; release y después renew por interfaz.";
            return task;
        }
        internal static string StepId(DhcpRenewalPlan plan, int ordinal) => ElevatedTaskCatalog.RenewDhcpId + "/" + plan.Adapters[ordinal / 2].InterfaceId + (ordinal % 2 == 0 ? "/release" : "/renew");
        internal static MaintenanceTask Command(DhcpRenewalPlan plan, int ordinal)
        {
            if (ordinal < 0 || ordinal >= plan.Adapters.Count * 2) throw new ArgumentOutOfRangeException(nameof(ordinal));
            var adapter = plan.Adapters[ordinal / 2];
            if (!DhcpRenewalPolicy.IsSafeName(adapter.Name)) throw new InvalidOperationException("Nombre de interfaz no seguro.");
            return new MaintenanceTask { Id = StepId(plan, ordinal), Name = adapter.Name + " · " + (ordinal % 2 == 0 ? "Liberar DHCP" : "Renovar DHCP"),
                Category = TaskCategory.Network, ImpactLevel = ImpactLevel.Configuration, TaskType = TaskType.Command,
                Command = ElevatedTaskCatalog.Get(ElevatedTaskCatalog.FlushDnsId).Command,
                Arguments = (ordinal % 2 == 0 ? "/release" : "/renew") + " \"" + adapter.Name + "\"", CanBeCancelled = false };
        }
        internal void ValidateCommandTarget(DhcpRenewalPlan plan, int ordinal)
        {
            var current = ReadPlan(); var adapter = plan.Adapters[ordinal / 2];
            if (!current.ReadSucceeded || !current.Adapters.Any(a => a.InterfaceId == adapter.InterfaceId && a.Name == adapter.Name && a.Kind == adapter.Kind))
                throw new InvalidOperationException("La interfaz confirmada ya no cumple los criterios DHCP; no se ejecuta el comando.");
        }
        internal DhcpVerification Verify(DhcpRenewalPlan plan, int ordinal)
        {
            try
            {
                var adapter = plan.Adapters[ordinal / 2];
                var nic = NetworkInterface.GetAllNetworkInterfaces().SingleOrDefault(n => Guid.TryParse(n.Id, out Guid id) && id.ToString("D") == adapter.InterfaceId && n.Name == adapter.Name);
                if (nic == null || !nic.Supports(NetworkInterfaceComponent.IPv4)) return DhcpVerification.Unknown;
                var properties = nic.GetIPProperties();
                if (!properties.GetIPv4Properties().IsDhcpEnabled) return DhcpVerification.NotConfirmed;
                var addresses = properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) && !a.Address.Equals(IPAddress.Any) && !(a.Address.GetAddressBytes()[0] == 169 && a.Address.GetAddressBytes()[1] == 254)).ToArray();
                if (ordinal % 2 == 0) return addresses.Length == 0 ? DhcpVerification.Released : DhcpVerification.NotConfirmed;
                bool renewed = nic.OperationalStatus == OperationalStatus.Up && addresses.Any(a => a.PrefixOrigin == PrefixOrigin.Dhcp && a.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Preferred);
                return renewed ? DhcpVerification.Renewed : DhcpVerification.NotConfirmed;
            }
            catch (Exception ex) { Log("No se pudo verificar resultado DHCP: " + ex); return DhcpVerification.Unknown; }
        }
    }
}
