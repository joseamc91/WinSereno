using System.Globalization;
namespace WinSereno.Models
{
    public sealed class NetworkAdapterInformation
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Kind { get; set; }
        public string Status { get; set; }
        public string LinkSpeed { get; set; } = WinSereno.Localization.LocalizationService.Source("Text.Unavailable");
        public string MaximumSpeed { get; set; } = WinSereno.Localization.LocalizationService.Source("Text.Unavailable");
        public string IPv4 { get; set; }
        public string Gateway { get; set; }
        public string Dns { get; set; }
        public string WifiDetails { get; set; }
        public bool IsConnected => Status == WinSereno.Localization.LocalizationService.Source("Text.Connected");
        public string CardName => string.IsNullOrWhiteSpace(Description) ? Name : Description;
        public bool HasCardName => !string.IsNullOrWhiteSpace(CardName) && !string.Equals(CardName, Kind, System.StringComparison.OrdinalIgnoreCase);
        public bool HasInterfaceName => !string.IsNullOrWhiteSpace(Name) && !string.Equals(Name, Kind, System.StringComparison.OrdinalIgnoreCase) && !string.Equals(Name, CardName, System.StringComparison.OrdinalIgnoreCase);
        public bool HasLinkSpeed => IsConnected && !string.IsNullOrWhiteSpace(LinkSpeed) && LinkSpeed != WinSereno.Localization.LocalizationService.Source("Text.Unavailable");
        public bool HasIPv4 => IsConnected && !string.IsNullOrWhiteSpace(IPv4);
        public bool HasGateway => !string.IsNullOrWhiteSpace(Gateway);
        public bool HasDns => !string.IsNullOrWhiteSpace(Dns);
        public bool HasAdditionalDetails => HasGateway || HasDns || HasWifiDetails;
        public string Heading => Name + WinSereno.Localization.LocalizationService.Source("Text.Separator") + Kind + WinSereno.Localization.LocalizationService.Source("Text.Separator") + Status;
        public string AddressDetails => "IPv4: " + Available(IPv4) + WinSereno.Localization.LocalizationService.Source("Text.Gateway148") + Available(Gateway) + WinSereno.Localization.LocalizationService.Source("Text.Dns149") + Available(Dns);
        public string SpeedDetails => WinSereno.Localization.LocalizationService.Source("Text.CurrentLink") + LinkSpeed;
        public bool HasWifiDetails => !string.IsNullOrEmpty(WifiDetails);
        private static string Available(string value) => string.IsNullOrEmpty(value) ? WinSereno.Localization.LocalizationService.Source("Text.Unavailable") : value;
        internal static string FormatSpeed(long? speed) => !speed.HasValue ? WinSereno.Localization.LocalizationService.Source("Text.Unavailable") : speed >= 1000000000 ? (speed.Value / 1000000000.0).ToString("0.##", CultureInfo.CurrentCulture) + " Gbps" : (speed.Value / 1000000.0).ToString("0.##", CultureInfo.CurrentCulture) + " Mbps";
    }
}
