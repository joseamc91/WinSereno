using System;
using System.IO;
using System.Text.RegularExpressions;
namespace WinSereno.Services
{
    internal static class WindowsVolumeResolver
    {
        private const string Error = "No se pudo determinar con seguridad la unidad de Windows. CHKDSK no se ejecutará.";
        internal static string FromWindowsPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Regex.IsMatch(path, @"\A[A-Za-z]:\\[^\x00-\x1F<>:""|?*]+\z") ||
                Array.Exists(path.Split('\\'), part => part == "." || part == "..")) throw new InvalidOperationException(Error);
            string root = Path.GetPathRoot(path);
            if (root == null || !Regex.IsMatch(root, @"\A[A-Za-z]:\\\z")) throw new InvalidOperationException(Error);
            return root.Substring(0, 2).ToUpperInvariant();
        }
        public static string Resolve()
        {
            try
            {
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string system = Environment.SystemDirectory;
                string volume = FromWindowsPath(windows);
                if (volume != FromWindowsPath(system) || !Directory.Exists(windows) || !Directory.Exists(system))
                    throw new InvalidOperationException(Error);
                return volume;
            }
            catch (Exception ex) { throw new InvalidOperationException(Error, ex); }
        }
    }
}
