using System.Globalization;
namespace WinSereno.Models
{
    public sealed class NetworkAdapterInformation
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Kind { get; set; }
        public string Status { get; set; }
        public string LinkSpeed { get; set; } = "No disponible";
        public string MaximumSpeed { get; set; } = "No disponible";
        public string IPv4 { get; set; }
        public string Gateway { get; set; }
        public string Dns { get; set; }
        public string WifiDetails { get; set; }
        public string Heading => Name + " · " + Kind + " · " + Status;
        public string AddressDetails => "IPv4: " + Available(IPv4) + " · Gateway: " + Available(Gateway) + " · DNS: " + Available(Dns);
        public string SpeedDetails => "Enlace actual: " + LinkSpeed + (Kind == "Ethernet" ? " · Capacidad máxima publicada: " + MaximumSpeed : "");
        public bool HasWifiDetails => !string.IsNullOrEmpty(WifiDetails);
        private static string Available(string value) => string.IsNullOrEmpty(value) ? "No disponible" : value;
        internal static string FormatSpeed(long? speed) => !speed.HasValue ? "No disponible" : speed >= 1000000000 ? (speed.Value / 1000000000.0).ToString("0.##", CultureInfo.CurrentCulture) + " Gbps" : (speed.Value / 1000000.0).ToString("0.##", CultureInfo.CurrentCulture) + " Mbps";
    }
}
