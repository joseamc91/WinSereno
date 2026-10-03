using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using WinSereno.Models;

namespace WinSereno.Services
{
    public interface ISystemInformationService
    {
        Task CollectAsync(IProgress<InformationUpdate> progress, CancellationToken cancellationToken);
    }
    public sealed class SystemInformationService : ISystemInformationService
    {
        private readonly ISessionLogger logger;
        private readonly NetworkInformationService network;
        public SystemInformationService(ISessionLogger logger)
        {
            this.logger = logger;
            network = new NetworkInformationService(logger);
        }
        public async Task CollectAsync(IProgress<InformationUpdate> progress, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            SystemQuery.Log(logger, "Inicio de recopilación de datos del sistema");
            try
            {
                await Task.WhenAll(
                    ReadAsync(InformationBlock.Windows, ReadWindows, progress, cancellationToken),
                    ReadAsync(InformationBlock.Cpu, ReadCpu, progress, cancellationToken),
                    ReadAsync(InformationBlock.Memory, ReadMemory, progress, cancellationToken),
                    ReadAsync(InformationBlock.Disks, ReadDisks, progress, cancellationToken),
                    ReadAsync(InformationBlock.Network, () => network.Read(), progress, cancellationToken),
                    ReadAsync(InformationBlock.Uptime, ReadUptime, progress, cancellationToken),
                    ReadAsync(InformationBlock.Gpu, ReadGpu, progress, cancellationToken));
            }
            finally { SystemQuery.Log(logger, "Finalización de recopilación de datos del sistema | Tiempo=" + watch.Elapsed); }
        }
        private Task ReadAsync(InformationBlock block, Func<object> read, IProgress<InformationUpdate> progress, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                if (cancellationToken.IsCancellationRequested) return;
                InformationUpdate update;
                try
                {
                    var data = read();
                    update = new InformationUpdate { Block = block, Data = data, Status = data == null ? InformationStatus.Unavailable : InformationStatus.Available };
                    SystemQuery.Log(logger, "Proveedor " + block + ": " + update.Status);
                }
                catch (Exception ex)
                {
                    SystemQuery.Log(logger, "Error proveedor " + block + ": " + ex);
                    update = new InformationUpdate { Block = block, Status = InformationStatus.Failed };
                }
                if (!cancellationToken.IsCancellationRequested) progress.Report(update);
            });
        }
        private object ReadWindows()
        {
            var result = new WindowsInformation();
            try
            {
                using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
                using (var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false))
                {
                    if (key != null)
                    {
                        result.ProductName = key.GetValue("ProductName") as string;
                        result.Edition = key.GetValue("EditionID") as string;
                        result.DisplayVersion = key.GetValue("DisplayVersion") as string;
                        result.Build = key.GetValue("CurrentBuildNumber") as string;
                        if (key.GetValue("UBR") is int revision && revision >= 0) result.Revision = revision;
                    }
                }
            }
            catch (Exception ex) { SystemQuery.Log(logger, "Windows: lectura de registro no disponible: " + ex); }
            try
            {
                var row = SystemQuery.Read("SELECT Caption, BuildNumber, OSArchitecture FROM Win32_OperatingSystem", "Caption", "BuildNumber", "OSArchitecture").FirstOrDefault();
                if (row != null)
                {
                    var caption = row["Caption"] as string;
                    if (!string.IsNullOrWhiteSpace(caption)) result.ProductName = caption.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase) ? caption.Substring(10) : caption;
                    if (string.IsNullOrEmpty(result.Build)) result.Build = row["BuildNumber"] as string;
                    result.Architecture = row["OSArchitecture"] as string;
                }
            }
            catch (Exception ex) { SystemQuery.Log(logger, "Windows: nombre comercial WMI no disponible: " + ex); }
            // Some Windows 11 installations retain a Windows 10 registry ProductName.
            // Restrict this fallback to that exact client product prefix; never rename Windows Server.
            if (result.ProductName != null && result.ProductName.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase) && int.TryParse(result.Build, out int build) && build >= 22000)
                result.ProductName = "Windows 11" + result.ProductName.Substring(10);
            if (string.IsNullOrWhiteSpace(result.Architecture)) result.Architecture = Environment.Is64BitOperatingSystem ? "64 bits" : "32 bits";
            return string.IsNullOrWhiteSpace(result.ProductName) && string.IsNullOrWhiteSpace(result.Build) ? null : result;
        }
        private object ReadCpu()
        {
            var rows = SystemQuery.Read("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor", "Name", "NumberOfCores", "NumberOfLogicalProcessors", "MaxClockSpeed");
            if (rows.Count == 0) return null;
            return new CpuInformation { Model = string.Join(" / ", rows.Select(r => r["Name"] as string).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct()),
                PhysicalCores = SumReliable(rows, "NumberOfCores"), LogicalProcessors = SumReliable(rows, "NumberOfLogicalProcessors"), MaxClockSpeedMHz = ReliableCpuClockSpeed(rows) };
        }
        private static int? ReliableCpuClockSpeed(System.Collections.Generic.IList<System.Collections.Generic.Dictionary<string, object>> rows)
        {
            int? common = null;
            foreach (var row in rows)
            {
                if (!row.TryGetValue("MaxClockSpeed", out object value) || value == null
                    || !int.TryParse(value.ToString(), out int mhz) || !SystemInformationPolicy.IsReasonableCpuClockSpeed(mhz)
                    || (common.HasValue && common.Value != mhz)) return null;
                common = mhz;
            }
            return common;
        }
        private object ReadGpu()
        {
            var adapters = SystemQuery.Read("SELECT Name, DriverVersion FROM Win32_VideoController", "Name", "DriverVersion")
                .Select(row => new GpuInformation { Name = row["Name"] as string, DriverVersion = row["DriverVersion"] as string }).ToList();
            return SummarizeGpu(adapters);
        }
        private static GpuInformation SummarizeGpu(System.Collections.Generic.IList<GpuInformation> adapters)
        {
            var named = adapters.Where(a => a != null && !string.IsNullOrWhiteSpace(a.Name)).ToList();
            if (named.Count == 0) return null;
            // Exclude only explicitly identified Windows software/remote adapters when other devices exist.
            // Basic Display Adapter may drive physical hardware and must remain eligible.
            var relevant = named.Where(a => !string.Equals(a.Name.Trim(), "Microsoft Remote Display Adapter", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(a.Name.Trim(), "Microsoft Basic Render Driver", StringComparison.OrdinalIgnoreCase)).ToList();
            if (relevant.Count == 0) relevant = named;
            var integrated = relevant.Where(a => IsIntelIntegratedGpu(a.Name)).ToList();
            var dedicated = relevant.Where(a => IsDedicatedGpu(a.Name)).ToList();
            // Split only a fully recognized combination; ambiguous devices stay together under GPU.
            bool canSplit = integrated.Count > 0 && dedicated.Count > 0 && integrated.Count + dedicated.Count == relevant.Count;
            string integratedName = canSplit ? string.Join(" / ", integrated.Select(a => a.Name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)) : null;
            bool isDedicated = dedicated.Count > 0 && (canSplit || dedicated.Count == relevant.Count);
            if (canSplit) relevant = dedicated;
            var names = relevant.Select(a => a.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
            var versions = relevant.Select(a => a.DriverVersion?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new GpuInformation
            {
                Name = string.Join(" / ", names),
                IntegratedName = integratedName,
                IsDedicated = isDedicated,
                // A single version is displayed only when it reliably applies to every listed adapter.
                DriverVersion = versions.Count == 1 && !string.IsNullOrWhiteSpace(versions[0]) ? versions[0] : null
            };
        }
        private static bool IsIntelIntegratedGpu(string name)
            => Regex.IsMatch(name.Trim(), @"^Intel(?:\(R\))?\s+(?:(?:UHD|HD)\s+Graphics\b|Iris(?:\(R\))?\s+(?:Xe\s+)?Graphics\b)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                && !Regex.IsMatch(name, @"\bMAX\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static bool IsDedicatedGpu(string name)
            => Regex.IsMatch(name.Trim(), @"^(?:NVIDIA\s+(?:GeForce|Quadro|RTX)\b|AMD\s+Radeon(?:\(TM\))?\s+(?:RX\s+\d{3,4}\b|Pro\s+(?:WX\s+\d+|W\d{4})\b))",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static int? SumReliable(System.Collections.Generic.IList<System.Collections.Generic.Dictionary<string, object>> rows, string field)
        {
            int total = 0;
            foreach (var row in rows)
            {
                if (row[field] == null || !int.TryParse(row[field].ToString(), out int count) || count <= 0) return null;
                total = checked(total + count);
            }
            return total;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);
        private object ReadMemory()
        {
            if (!GetPhysicallyInstalledSystemMemory(out ulong kilobytes) || kilobytes == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return MemoryModuleReader.Enrich(checked(kilobytes * 1024), MemoryModuleReader.Read, logger);
        }
        public DiskCollection ReadDisks()
        {
            var result = new DiskCollection();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    var driveType = drive.DriveType;
                    if ((driveType != DriveType.Fixed && driveType != DriveType.Removable) || !drive.IsReady) continue;
                    var total = drive.TotalSize;
                    if (total <= 0) continue;
                    string label = null;
                    try { label = drive.VolumeLabel; } catch (Exception ex) { SystemQuery.Log(logger, "Etiqueta de volumen " + drive.Name + " no disponible: " + ex.Message); }
                    result.Volumes.Add(new DiskInformation { DriveType = driveType, Unit = drive.Name.TrimEnd('\\'), Label = label, TotalBytes = total, FreeBytes = drive.TotalFreeSpace });
                    SystemQuery.Log(logger, "Unidad local detectada: " + drive.Name);
                }
                catch (Exception ex) { result.HasErrors = true; SystemQuery.Log(logger, "Error proveedor Disks en unidad " + drive.Name + ": " + ex); }
            }
            SystemQuery.Log(logger, "Discos: " + result.Volumes.Count + " volúmenes locales listos");
            return result;
        }
        private object ReadUptime()
        {
            var row = SystemQuery.Read("SELECT LastBootUpTime FROM Win32_OperatingSystem", "LastBootUpTime").FirstOrDefault();
            if (row?["LastBootUpTime"] == null) return null;
            var boot = ManagementDateTimeConverter.ToDateTime(row["LastBootUpTime"].ToString());
            var uptime = DateTime.UtcNow - boot.ToUniversalTime();
            return uptime < TimeSpan.Zero ? null : new UptimeInformation { Uptime = uptime };
        }
    }
}
