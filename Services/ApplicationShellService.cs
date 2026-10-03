using System;
using System.Diagnostics;
using System.IO;

namespace WinSereno.Services
{
    public interface IApplicationShell
    {
        void OpenLogs();
        void OpenApplicationFolder();
        void OpenGitHub();
        void OpenReleases();
    }
    public interface IPreferencesDialogs { bool ConfirmResetPreferences(); }
    public sealed class ApplicationShellService : IApplicationShell
    {
        private readonly PortableStorage storage;
        private readonly Action<ProcessStartInfo> launch;
        public const string GitHubUrl = "https://github.com/joseamc91/WinSereno";
        public const string ReleasesUrl = GitHubUrl + "/releases";
        public ApplicationShellService(PortableStorage storage) : this(storage, info => { using (var process = Process.Start(info)) { } }) { }
        internal ApplicationShellService(PortableStorage storage, Action<ProcessStartInfo> launch)
        { this.storage = storage ?? throw new ArgumentNullException(nameof(storage)); this.launch = launch ?? throw new ArgumentNullException(nameof(launch)); }
        public void OpenLogs() => new LogsFolderService(storage, launch).Open();
        public void OpenApplicationFolder()
        {
            var directory = PortablePathValidation.ValidateBase(storage.BaseDirectory);
            if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("La carpeta portable no está disponible o está redirigida.");
            Open(directory);
        }
        private void Open(string target) => launch(new ProcessStartInfo { FileName = target, UseShellExecute = true, Verb = "open" });
        public void OpenGitHub() => Open(GitHubUrl);
        public void OpenReleases() => Open(ReleasesUrl);
    }
}
