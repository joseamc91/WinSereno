using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
namespace WinSereno.Services
{
    public sealed class LogsFolderService
    {
        private readonly PortableStorage storage;
        private readonly Action<ProcessStartInfo> launch;
        public LogsFolderService(PortableStorage storage) : this(storage, info => { using (var process = Process.Start(info)) { } }) { }
        internal LogsFolderService(PortableStorage storage, Action<ProcessStartInfo> launch)
        { this.storage = storage ?? throw new ArgumentNullException(nameof(storage)); this.launch = launch ?? throw new ArgumentNullException(nameof(launch)); }
        public void Open()
        {
            string basePath = storage.BaseDirectory;
            if (string.IsNullOrWhiteSpace(basePath) || !Regex.IsMatch(basePath, @"\A[A-Za-z]:\\") ||
                basePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Directory.Exists(basePath))
                throw new IOException("No se pudo determinar una carpeta válida para la aplicación. No se abrirá otra ubicación.");
            string expected = Path.Combine(Path.GetFullPath(basePath), "Logs");
            string actual = Path.GetFullPath(storage.LogsDirectory);
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new IOException("La carpeta de logs no corresponde a la ubicación portable esperada.");
            if (!Directory.Exists(actual)) Directory.CreateDirectory(actual);
            if ((File.GetAttributes(actual) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("La carpeta de logs es un enlace o punto de análisis. No se abrirá una ubicación redirigida.");
            launch(new ProcessStartInfo { FileName = actual, UseShellExecute = true, Verb = "open" });
        }
    }
}
