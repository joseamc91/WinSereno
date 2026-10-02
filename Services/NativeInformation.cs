using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WinSereno.Services
{
    internal sealed class WifiInformation
    {
        public string Ssid { get; set; }
        public uint? SignalQuality { get; set; }
        public int? Rssi { get; set; }
        public uint ReceiveRate { get; set; }
        public uint TransmitRate { get; set; }
    }
    internal static class NativeWifiInformation
    {
        [StructLayout(LayoutKind.Sequential)] private struct Ssid
        {
            public uint Length;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Bytes;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Association
        {
            public Ssid Ssid;
            public int BssType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
            public int PhyType;
            public uint PhyIndex, SignalQuality, ReceiveRate, TransmitRate;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Security
        {
            [MarshalAs(UnmanagedType.Bool)] public bool Enabled;
            [MarshalAs(UnmanagedType.Bool)] public bool OneX;
            public int Authentication, Cipher;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Connection
        {
            public int State, Mode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Profile;
            public Association Association;
            public Security Security;
        }
        [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
        [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr handle, ref Guid guid, int opcode, IntPtr reserved, out uint size, out IntPtr data, out int type);
        [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr data);
        [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
        public static WifiInformation Read(string interfaceId)
        {
            if (!Guid.TryParse(interfaceId, out Guid guid)) return null;
            uint error = WlanOpenHandle(2, IntPtr.Zero, out uint version, out IntPtr handle);
            if (error != 0) throw new Win32Exception((int)error);
            try
            {
                // Newer Windows versions can display a location-consent prompt for
                // current_connection. Avoid that API there; RSSI does not expose SSID/BSSID.
                if (!CanQueryConnectionWithoutLocationConsent())
                {
                    error = WlanQueryInterface(handle, ref guid, 0x10000102, IntPtr.Zero, out uint rssiSize, out IntPtr rssiData, out int rssiType);
                    if (error != 0) throw new Win32Exception((int)error);
                    try
                    {
                        if (rssiSize != sizeof(int)) return null;
                        int rssi = Marshal.ReadInt32(rssiData);
                        return new WifiInformation { Rssi = rssi < 0 && rssi >= -120 ? (int?)rssi : null };
                    }
                    finally { WlanFreeMemory(rssiData); }
                }
                // wlan_intf_opcode_current_connection = 7. No scans or configuration APIs.
                error = WlanQueryInterface(handle, ref guid, 7, IntPtr.Zero, out uint size, out IntPtr data, out int type);
                if (error != 0) throw new Win32Exception((int)error);
                try
                {
                    if (size < Marshal.SizeOf<Connection>()) return null;
                    var connection = Marshal.PtrToStructure<Connection>(data);
                    if (connection.State != 1 || connection.Association.Ssid.Length > 32 || connection.Association.SignalQuality > 100) return null;
                    var ssid = connection.Association.Ssid;
                    string name;
                    try { name = new UTF8Encoding(false, true).GetString(ssid.Bytes, 0, (int)ssid.Length); }
                    catch (DecoderFallbackException) { name = "SSID no representable como texto UTF-8"; }
                    return new WifiInformation { Ssid = name, SignalQuality = connection.Association.SignalQuality,
                        ReceiveRate = connection.Association.ReceiveRate, TransmitRate = connection.Association.TransmitRate };
                }
                finally { WlanFreeMemory(data); }
            }
            finally { WlanCloseHandle(handle, IntPtr.Zero); }
        }
        private static bool CanQueryConnectionWithoutLocationConsent()
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
                using (var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false))
                    return int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out int build) && build > 0 && build < 25976;
            }
            catch { return false; }
        }
    }
}
