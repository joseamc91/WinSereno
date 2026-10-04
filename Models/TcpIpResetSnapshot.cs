using System;
using System.Collections.Generic;
using System.Linq;

namespace WinSereno.Models
{
    public enum Ipv4ConfigurationMode { Dhcp, Manual, Unknown }

    public sealed class TcpIpAdapterConfiguration
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public Ipv4ConfigurationMode Mode { get; }
        public string IPv4 { get; }
        public string Mask { get; }
        public string Gateway { get; }
        public string Dns { get; }
        public string Heading => Name == Description || string.IsNullOrWhiteSpace(Description) ? Name : Name + WinSereno.Localization.LocalizationService.Source("Text.Separator") + Description;
        public string ConfigurationLabel => Mode == Ipv4ConfigurationMode.Manual ? WinSereno.Localization.LocalizationService.Source("Text.DhcpDisabledManualConfiguration")
            : Mode == Ipv4ConfigurationMode.Dhcp ? WinSereno.Localization.LocalizationService.Source("Text.DhcpEnabled") : WinSereno.Localization.LocalizationService.Source("Text.ConfigurationUnavailable");
        public TcpIpAdapterConfiguration(string id, string name, string description, Ipv4ConfigurationMode mode,
            string ipv4, string mask, string gateway, string dns)
        {
            Id = id ?? ""; Name = name ?? WinSereno.Localization.LocalizationService.Source("Text.Unavailable"); Description = description; Mode = mode;
            IPv4 = Available(ipv4); Mask = Available(mask); Gateway = Available(gateway); Dns = Available(dns);
        }
        private static string Available(string value) => string.IsNullOrWhiteSpace(value) ? WinSereno.Localization.LocalizationService.Source("Text.Unavailable") : value;
    }

    public sealed class TcpIpResetSnapshot
    {
        public IReadOnlyList<TcpIpAdapterConfiguration> Interfaces { get; }
        public IReadOnlyList<TcpIpAdapterConfiguration> WarningInterfaces { get; }
        public bool ReadComplete { get; }
        public bool RequiresWarning => !ReadComplete || Interfaces.Any(a => a.Mode != Ipv4ConfigurationMode.Dhcp);
        public bool IsIndeterminate => !ReadComplete || Interfaces.Any(a => a.Mode == Ipv4ConfigurationMode.Unknown);
        public string WarningTitle => IsIndeterminate ? WinSereno.Localization.LocalizationService.Source("Text.Ipv4ConfigurationNotFullyChecked") : WinSereno.Localization.LocalizationService.Source("Text.ManualIpv4ConfigurationDetected");
        public string WarningMessage => IsIndeterminate
            ? WinSereno.Localization.LocalizationService.Source("Text.ThisComputerSIpv4ConfigurationCouldNotBe")
            : WarningInterfaces.Count == 1
                ? WinSereno.Localization.LocalizationService.Source("Text.ThisInterfaceUsesAManualIpv4ConfigurationResetting")
                : WinSereno.Localization.LocalizationService.Source("Text.InterfacesWithManualIpv4ConfigurationWereDetectedResetting");
        public string UnavailableNotice => IsIndeterminate ? WinSereno.Localization.LocalizationService.Source("Text.ConfigurationUnavailableOrIncompleteInterfacesAreNotAssumed") : "";
        public TcpIpResetSnapshot(IEnumerable<TcpIpAdapterConfiguration> interfaces, bool readComplete)
        {
            Interfaces = Array.AsReadOnly(interfaces.ToArray()); ReadComplete = readComplete;
            WarningInterfaces = Array.AsReadOnly(Interfaces.Where(a => a.Mode != Ipv4ConfigurationMode.Dhcp).ToArray());
        }
    }
}
