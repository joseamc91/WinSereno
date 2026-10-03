using System;
using System.Collections.Generic;

namespace WinSereno.Models
{
    public enum InformationBlock { Windows, Cpu, Memory, Disks, Network, Uptime, Gpu }
    public enum InformationStatus { Available, Unavailable, Failed }
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
        public int? MaxClockSpeedMHz { get; set; }
    }
    public sealed class GpuInformation
    {
        public string Name { get; set; }
        public string DriverVersion { get; set; }
        public string IntegratedName { get; set; }
        public bool IsDedicated { get; set; }
    }
    public sealed class MemoryInformation
    {
        public ulong InstalledBytes { get; set; }
        public IList<MemoryModuleInformation> Modules { get; set; } = new List<MemoryModuleInformation>();
    }
    public sealed class MemoryModuleInformation
    {
        public ulong CapacityBytes { get; set; }
        public uint SmbiosMemoryType { get; set; }
        public uint ConfiguredSpeed { get; set; }
    }
    public sealed class DiskInformation
    {
        public System.IO.DriveType DriveType { get; set; } = System.IO.DriveType.Fixed;
        public string Unit { get; set; }
        public string Label { get; set; }
        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }
        public double FreePercentage => TotalBytes > 0 ? 100.0 * FreeBytes / TotalBytes : 0;
        public bool NeedsAttention => FreePercentage < SystemInformationPolicy.LowDiskSpacePercentage;
    }
    public static class SystemInformationPolicy
    {
        public const double LowDiskSpacePercentage = 10.0;
        public static bool IsReasonableCpuClockSpeed(int mhz) => mhz >= 100 && mhz <= 20000;
    }
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
}
