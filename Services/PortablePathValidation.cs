using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
namespace WinSereno.Services
{
    internal static class PortablePathValidation
    {
        internal static string ValidateBase(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Regex.IsMatch(path, @"\A[A-Za-z]:\\") ||
                path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
                Array.Exists(path.Split('\\'), part => part == "." || part == ".."))
                throw new IOException("La carpeta de la aplicación debe ser una ruta absoluta local válida.");
            string full = Path.GetFullPath(path);
            if (!Directory.Exists(full) || new DriveInfo(Path.GetPathRoot(full)).DriveType == DriveType.Network)
                throw new IOException("No se pudo resolver una carpeta local existente para la aplicación.");
            return full;
        }
        internal static void GuardAncestors(string path, IList<SafeFileHandle> handles)
        {
            var paths = new Stack<string>();
            for (var dir = new DirectoryInfo(path); dir != null; dir = dir.Parent) paths.Push(dir.FullName);
            while (paths.Count > 0) handles.Add(Open(paths.Pop(), true, false));
        }
        internal static SafeFileHandle Open(string path, bool directory, bool writable)
        {
            var handle = CreateFile(path, writable ? 0x40000080u : 0x80u, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid) { int code = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(code); }
            try
            {
                if (!GetFileInformationByHandle(handle, out Info info)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if ((info.Attributes & 0x400) != 0 || ((info.Attributes & 0x10) != 0) != directory)
                    throw new IOException("La ubicación gestionada es un enlace, punto de análisis o un tipo de archivo inesperado.");
                return handle;
            }
            catch { handle.Dispose(); throw; }
        }
        [StructLayout(LayoutKind.Sequential)] private struct Info
        { public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true, EntryPoint="CreateFileW")]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Info info);
    }
}
