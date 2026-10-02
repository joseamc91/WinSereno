using System;
using System.Runtime.InteropServices;
namespace WinSereno.Services
{
    public sealed class RemoteSessionInformation
    {
        public bool IsRemote { get; internal set; }
        public bool IsKnown { get; internal set; }
        public string Source { get; internal set; }
    }
    public static class RemoteSessionService
    {
        public const string Warning = "Esta acción puede interrumpir la conexión remota actual.";
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);
        [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr buffer);
        public static RemoteSessionInformation Read()
        {
            bool remote = false; bool known = false;
            try { remote = GetSystemMetrics(0x1000) != 0; known = true; } catch (Exception) { }
            IntPtr buffer = IntPtr.Zero;
            try
            {
                // Current process session only; never enumerate users or record client addresses.
                if (WTSQuerySessionInformation(IntPtr.Zero, -1, 16, out buffer, out int bytes) && bytes >= 2)
                {
                    short protocol = Marshal.ReadInt16(buffer);
                    return new RemoteSessionInformation { IsKnown = protocol == 0 || protocol == 1 || protocol == 2 || known,
                        IsRemote = remote || protocol == 1 || protocol == 2, Source = "WTSClientProtocolType / SM_REMOTESESSION" };
                }
            }
            catch (Exception) { }
            finally { if (buffer != IntPtr.Zero) WTSFreeMemory(buffer); }
            return new RemoteSessionInformation { IsKnown = known, IsRemote = remote, Source = "SM_REMOTESESSION" };
        }
        public static string GetWarning(RemoteSessionInformation info) => info.IsRemote ? Warning : !info.IsKnown ?
            "No se pudo comprobar si la sesión es remota. Esta acción puede interrumpir una conexión remota." : null;
    }
}
