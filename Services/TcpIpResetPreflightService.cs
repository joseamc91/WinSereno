using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using WinSereno.Models;

namespace WinSereno.Services
{
    public interface ITcpIpResetPreflight { TcpIpResetSnapshot Read(); }
    public interface ITcpIpResetDialogs { bool ConfirmTcpIpReset(TcpIpResetSnapshot snapshot); }

    public sealed class TcpIpResetPreflightService : ITcpIpResetPreflight
    {
        private readonly ISessionLogger logger;
        public TcpIpResetPreflightService(ISessionLogger logger) { this.logger = logger; }
        public static Ipv4ConfigurationMode Classify(bool? windowsDhcp, bool? interfaceDhcp)
            => !windowsDhcp.HasValue || !interfaceDhcp.HasValue || windowsDhcp != interfaceDhcp
                ? Ipv4ConfigurationMode.Unknown : windowsDhcp.Value ? Ipv4ConfigurationMode.Dhcp : Ipv4ConfigurationMode.Manual;

        public TcpIpResetSnapshot Read()
        {
            var adapters = new List<TcpIpAdapterConfiguration>(); bool complete = true;
            try
            {
                var physical = SystemQuery.Read("SELECT GUID, PhysicalAdapter, ServiceName FROM Win32_NetworkAdapter", "GUID", "PhysicalAdapter", "ServiceName");
                var configurations = SystemQuery.Read("SELECT SettingID, DHCPEnabled FROM Win32_NetworkAdapterConfiguration", "SettingID", "DHCPEnabled");
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    string kind = nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "Ethernet" : nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "";
                    var rows = physical.Where(r => string.Equals(Convert.ToString(r["GUID"]), nic.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
                    string driver = rows.Length == 1 ? Convert.ToString(rows[0]["ServiceName"]) : null;
                    // Reuse the shared physical/name/driver exclusions; active and DHCP are not prerequisites here.
                    if (!DhcpRenewalPolicy.IsEligible(true, true, true, true, kind, nic.Name, nic.Description, driver)) continue;
                    bool knownPhysical = rows.Length == 1 && rows[0]["PhysicalAdapter"] is bool;
                    if (knownPhysical && !(bool)rows[0]["PhysicalAdapter"]) continue;
                    try
                    {
                        var ip = nic.GetIPProperties(); var ipv4 = ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).ToArray();
                        if (ipv4.Length == 0) continue;
                        var config = configurations.Where(r => string.Equals(Convert.ToString(r["SettingID"]), nic.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
                        bool? windowsDhcp = config.Length == 1 && config[0]["DHCPEnabled"] is bool ? (bool?)config[0]["DHCPEnabled"] : null;
                        bool? interfaceDhcp = null; try { interfaceDhcp = ip.GetIPv4Properties()?.IsDhcpEnabled; } catch (NetworkInformationException) { }
                        var mode = knownPhysical ? Classify(windowsDhcp, interfaceDhcp) : Ipv4ConfigurationMode.Unknown;
                        if (mode == Ipv4ConfigurationMode.Unknown) complete = false;
                        adapters.Add(new TcpIpAdapterConfiguration(nic.Id, nic.Name, nic.Description, mode,
                            string.Join(", ", ipv4.Select(a => a.Address.ToString())), string.Join(", ", ipv4.Select(a => a.IPv4Mask?.ToString() ?? "No disponible")),
                            string.Join(", ", ip.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork).Select(g => g.Address.ToString())),
                            string.Join(", ", ip.DnsAddresses.Select(a => a.ToString()))));
                    }
                    catch (Exception ex)
                    {
                        complete = false;
                        adapters.Add(new TcpIpAdapterConfiguration(nic.Id, nic.Name, nic.Description, Ipv4ConfigurationMode.Unknown, null, null, null, null));
                        Log("Preflight TCP/IP | Interfaz no comprobada: " + nic.Name + " | " + ex.GetType().Name);
                    }
                }
            }
            catch (Exception ex) { complete = false; Log("Preflight TCP/IP incompleto | " + ex.GetType().Name); }
            return new TcpIpResetSnapshot(adapters, complete);
        }
        private void Log(string text) { if (logger != null) SystemQuery.Log(logger, text); }

        public static string Fingerprint(TcpIpResetSnapshot snapshot)
        {
            // Bind consent to interface identities and DHCP modes, not mutable IP addresses.
            var context = ElevatedTaskCatalog.ResetTcpIpId + "|" + snapshot.ReadComplete + "|" + string.Join("|",
                snapshot.Interfaces.OrderBy(a => a.Id, StringComparer.OrdinalIgnoreCase).Select(a => a.Id.ToUpperInvariant() + ":" + (int)a.Mode));
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(context))).Replace("-", "");
        }
        public static void ValidateContext(TcpIpResetSnapshot current, string fingerprint, bool warningAccepted)
        {
            if (fingerprint == null || fingerprint.Length != 64 || fingerprint != Fingerprint(current) || (current.RequiresWarning && !warningAccepted))
                throw new InvalidOperationException("La configuración IPv4 confirmada ha cambiado o no se confirmó su riesgo. No se ejecuta TCP/IP; revisa la configuración y vuelve a confirmar.");
        }
    }

    internal sealed class TcpIpResetApproval
    {
        internal string Fingerprint { get; }
        internal bool WarningAccepted { get; }
        private int consumed;
        internal TcpIpResetApproval(TcpIpResetSnapshot snapshot, bool warningAccepted)
        {
            TcpIpResetPreflightService.ValidateContext(snapshot, TcpIpResetPreflightService.Fingerprint(snapshot), warningAccepted);
            Fingerprint = TcpIpResetPreflightService.Fingerprint(snapshot); WarningAccepted = warningAccepted;
        }
        internal void Consume() { if (System.Threading.Interlocked.Exchange(ref consumed, 1) != 0) throw new InvalidOperationException("La confirmación TCP/IP ya se utilizó."); }
    }
}
