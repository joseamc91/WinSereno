using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WinSereno.Models;

namespace WinSereno.Services
{
    internal static class MemoryModuleReader
    {
        public static IList<MemoryModuleInformation> Read()
        {
            return SystemQuery.Read("SELECT Capacity, SMBIOSMemoryType, ConfiguredClockSpeed FROM Win32_PhysicalMemory",
                "Capacity", "SMBIOSMemoryType", "ConfiguredClockSpeed")
                .Select(row => new MemoryModuleInformation
                {
                    CapacityBytes = Number(row["Capacity"]),
                    SmbiosMemoryType = SmallNumber(row["SMBIOSMemoryType"]),
                    ConfiguredSpeed = SmallNumber(row["ConfiguredClockSpeed"])
                }).ToList();
        }
        public static MemoryInformation Enrich(ulong installedBytes, Func<IList<MemoryModuleInformation>> readModules, ISessionLogger logger)
        {
            var memory = new MemoryInformation { InstalledBytes = installedBytes };
            try { memory.Modules = readModules() ?? new List<MemoryModuleInformation>(); }
            catch (Exception ex)
            {
                // Module metadata is optional; a WMI failure must not discard the native installed total.
                SystemQuery.Log(logger, "No se pudieron consultar los detalles de módulos RAM: " + ex);
            }
            return memory;
        }
        private static ulong Number(object value)
            => value != null && ulong.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out ulong number) ? number : 0;
        private static uint SmallNumber(object value)
        {
            ulong number = Number(value);
            return number < uint.MaxValue ? (uint)number : 0;
        }
    }
}
