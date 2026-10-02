using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using WinSereno.Models;

namespace WinSereno.Services
{
    public sealed class NetworkInformationService
    {
        private readonly ISessionLogger logger;
        public NetworkInformationService(ISessionLogger logger) { this.logger = logger; }
        public NetworkInformation Read()
        {
            var physicalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool physicalKnown = false;
            try
            {
                foreach (var row in SystemQuery.Read("SELECT GUID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE", "GUID"))
                    if (row["GUID"] != null) physicalIds.Add(row["GUID"].ToString().Trim('{', '}'));
                physicalKnown = true;
            }
            catch (Exception ex) { SystemQuery.Log(logger, "Identificación física de adaptadores no disponible: " + ex); }

            var candidates = new List<NetworkInformation>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (nic.OperationalStatus != OperationalStatus.Up ||
                        (nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet && nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)) continue;
                    if (LooksVirtual(nic.Description + " " + nic.Name)) continue;
                    if (!physicalKnown || !physicalIds.Contains(nic.Id.Trim('{', '}'))) continue;
                    var properties = nic.GetIPProperties();
                    if (!properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))) continue;
                    var addresses = properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) &&
                        !a.Address.Equals(IPAddress.Any) && !(a.Address.GetAddressBytes()[0] == 169 && a.Address.GetAddressBytes()[1] == 254))
                        .Select(a => a.Address.ToString()).Distinct().ToArray();
                    var gateways = properties.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)).Select(g => g.Address.ToString()).Distinct().ToArray();
                    candidates.Add(new NetworkInformation { InterfaceId = nic.Id, Name = nic.Name, Description = nic.Description,
                        GatewayIPv4 = gateways.Length == 1 ? gateways[0] : null,
                        DnsServers = properties.DnsAddresses.Select(a => a.ToString()).ToArray(),
                        Kind = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Ethernet",
                        SpeedBitsPerSecond = nic.Speed > 0 ? (long?)nic.Speed : null, IPv4 = addresses.Length == 1 ? addresses[0] : null });
                }
                catch (Exception ex) { SystemQuery.Log(logger, "Error consulta de adaptador: " + ex.GetType().Name + " | " + ex.Message); }
            }
            // Only a unique, confirmed physical Ethernet/Wi-Fi adapter with IPv4 gateway qualifies.
            // Multiple eligible adapters are deliberately ambiguous: do not infer routing priority.
            if (candidates.Count == 1)
            {
                var selected = candidates[0];
                SystemQuery.Log(logger, "Adaptador principal seleccionado: " + selected.Name + " | " + selected.Kind + " | IPv4=" + (selected.IPv4 ?? "no inequívoca"));
                return selected;
            }
            var neutral = !physicalKnown ? "No se pudo identificar un adaptador físico principal" : candidates.Count > 1 ? "Varios adaptadores activos; principal no determinado" : "Sin adaptador físico principal identificado";
            SystemQuery.Log(logger, "Red: " + neutral);
            return new NetworkInformation { NeutralMessage = neutral };
        }
        public IList<NetworkAdapterInformation> ReadAdapters()
        {
            var adapters = new List<NetworkAdapterInformation>();
            var ids = new HashSet<string>(SystemQuery.Read("SELECT GUID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE", "GUID")
                .Where(r => r["GUID"] != null).Select(r => r["GUID"].ToString().Trim('{', '}')), StringComparer.OrdinalIgnoreCase);
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if ((nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet && nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) ||
                    (nic.Description + " " + nic.Name).IndexOf("Bluetooth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    LooksVirtual(nic.Description + " " + nic.Name) || !ids.Contains(nic.Id.Trim('{', '}'))) continue;
                var item = new NetworkAdapterInformation { Name = nic.Name, Description = nic.Description,
                    Kind = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Ethernet",
                    Status = nic.OperationalStatus == OperationalStatus.Up ? "Conectado" : nic.OperationalStatus == OperationalStatus.Down ? "Desconectado" : "No conectado (" + nic.OperationalStatus + ")" };
                adapters.Add(item);
                try
                {
                    var properties = nic.GetIPProperties();
                    item.IPv4 = string.Join(", ", properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)).Select(a => a.Address.ToString()).Distinct());
                    item.Gateway = string.Join(", ", properties.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)).Select(g => g.Address.ToString()).Distinct());
                    item.Dns = string.Join(", ", properties.DnsAddresses.Select(a => a.ToString()).Distinct());
                    if (nic.OperationalStatus == OperationalStatus.Up && nic.Speed > 0) item.LinkSpeed = NetworkAdapterInformation.FormatSpeed(nic.Speed);
                    if (item.Kind == "Ethernet") item.MaximumSpeed = NetworkAdapterInformation.FormatSpeed(ReadMaximumSpeed(nic.Id));
                    if (item.Kind == "Wi-Fi" && nic.OperationalStatus == OperationalStatus.Up)
                    {
                        try
                        {
                            var wifi = NativeWifiInformation.Read(nic.Id);
                            if (wifi != null) item.WifiDetails = "SSID: " + (wifi.Ssid ?? "no disponible") + " · Señal: " +
                                (wifi.SignalQuality.HasValue ? wifi.SignalQuality + " %" : wifi.Rssi.HasValue ? wifi.Rssi + " dBm" : "no disponible") +
                                " · Recepción/envío: " + NetworkAdapterInformation.FormatSpeed(wifi.ReceiveRate > 0 ? (long?)wifi.ReceiveRate * 1000 : null) + " / " +
                                NetworkAdapterInformation.FormatSpeed(wifi.TransmitRate > 0 ? (long?)wifi.TransmitRate * 1000 : null);
                        }
                        catch (Exception ex) { SystemQuery.Log(logger, "Información Wi-Fi no disponible: " + ex.GetType().Name); item.WifiDetails = "SSID y señal no disponibles."; }
                    }
                    SystemQuery.Log(logger, "Red adaptador físico: " + item.Name + " | " + item.Kind + " | " + item.Status + " | Enlace=" + item.LinkSpeed + " | Máxima=" + item.MaximumSpeed);
                }
                catch (Exception ex) { SystemQuery.Log(logger, "Error al leer adaptador " + nic.Name + ": " + ex); }
            }
            return adapters.OrderBy(a => a.Status != "Conectado").ThenBy(a => a.Name).ToArray();
        }
        internal long? ReadMaximumSpeed(string interfaceId)
        {
            try
            {
                if (!Guid.TryParse(interfaceId, out Guid id)) return null;
                var rows = SystemQuery.ReadNamespace(@"\\.\root\StandardCimv2", "SELECT InterfaceGuid, MaxSpeed FROM MSFT_NetAdapter WHERE HardwareInterface = TRUE", "InterfaceGuid", "MaxSpeed");
                var matches = rows.Where(r => Guid.TryParse(r["InterfaceGuid"] as string, out Guid other) && other == id).ToArray();
                if (matches.Length == 1 && matches[0]["MaxSpeed"] != null && long.TryParse(matches[0]["MaxSpeed"].ToString(), out long speed) && speed > 0) return speed;
            }
            catch (Exception ex) { SystemQuery.Log(logger, "Capacidad Ethernet no disponible: " + ex.GetType().Name + " | " + ex.Message); }
            return null;
        }
        private static bool LooksVirtual(string text)
        {
            return new[] { "virtual", "hyper-v", "vmware", "virtualbox", "vpn", "tunnel", "tap-", "wireguard", "wintun" }
                .Any(marker => text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
