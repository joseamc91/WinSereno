using System;
using System.Collections.Generic;

namespace WinSereno.Models
{
    public enum InformationBlock { Windows, Cpu, Memory, Disks, Network, Uptime, Restart }
    public enum InformationStatus { Available, Unavailable, Failed }
    public enum RestartPendingStatus { Pending, Possible, NotPending, Unknown }
    public sealed class InformationUpdate
    {
        public InformationBlock Block { get; set; }
        public InformationStatus Status { get; set; }
        public object Data { get; set; }
    }
    public sealed class WindowsInformation
    {
        public string ProductName { get; set; }
        public string Edition { get; set; }
        public string DisplayVersion { get; set; }
        public string Build { get; set; }
        public int? Revision { get; set; }
        public string Architecture { get; set; }
    }
    public sealed class CpuInformation
    {
        public string Model { get; set; }
        public int? PhysicalCores { get; set; }
        public int? LogicalProcessors { get; set; }
    }
    public sealed class MemoryInformation { public ulong InstalledBytes { get; set; } }
    public sealed class DiskInformation
    {
        public string Unit { get; set; }
        public string Label { get; set; }
        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }
        public double FreePercentage => TotalBytes > 0 ? 100.0 * FreeBytes / TotalBytes : 0;
        public bool NeedsAttention => FreePercentage < SystemInformationPolicy.LowDiskSpacePercentage;
    }
    public static class SystemInformationPolicy { public const double LowDiskSpacePercentage = 10.0; }
    public sealed class DiskCollection { public IList<DiskInformation> Volumes { get; } = new List<DiskInformation>(); public bool HasErrors { get; set; } }
    public sealed class NetworkInformation
    {
        public string InterfaceId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Kind { get; set; }
        public long? SpeedBitsPerSecond { get; set; }
        public string IPv4 { get; set; }
        public string GatewayIPv4 { get; set; }
        public string[] DnsServers { get; set; } = new string[0];
        public string NeutralMessage { get; set; }
    }
    public sealed class UptimeInformation { public TimeSpan Uptime { get; set; } }
    public sealed class RestartPendingInformation
    {
        public RestartPendingStatus Status { get; set; }
        public IList<string> PendingIndicators { get; } = new List<string>();
    }
}
