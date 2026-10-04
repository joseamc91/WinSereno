using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace WinSereno.Services
{
    [DataContract]
    public sealed class AppSettings
    {
        [DataMember] public string Theme { get; set; } = "Light";
        [DataMember] public string Language { get; set; } = "es";
    }

    public sealed class PortableStorage
    {
        public string BaseDirectory { get; }
        public string LogsDirectory => Path.Combine(BaseDirectory, "Logs");
        public string ConfigPath => Path.Combine(BaseDirectory, "config.json");
        private readonly Func<string, Stream> createProbe;
        private readonly Action<string> deleteProbe;
        public PortableStorage(string baseDirectory) : this(baseDirectory,
            path => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None), File.Delete) { }
        internal PortableStorage(string baseDirectory, Func<string, Stream> createProbe, Action<string> deleteProbe)
        { BaseDirectory = baseDirectory; this.createProbe = createProbe; this.deleteProbe = deleteProbe; }

        public bool CheckWritable(out string error)
        {
            var guards = new System.Collections.Generic.List<Microsoft.Win32.SafeHandles.SafeFileHandle>();
            string stage = "la carpeta de la aplicación";
            try
            {
                string root = PortablePathValidation.ValidateBase(BaseDirectory);
                PortablePathValidation.GuardAncestors(root, guards);
                Probe(root);
                stage = "la configuración";
                ValidateConfigFile(ConfigPath, guards);
                ValidateConfigFile(ConfigPath + ".tmp", guards);
                stage = "la carpeta Logs";
                Directory.CreateDirectory(LogsDirectory);
                guards.Add(PortablePathValidation.Open(LogsDirectory, true, false));
                Probe(LogsDirectory);
                error = null; return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException ||
                ex is ArgumentException || ex is NotSupportedException || ex is System.ComponentModel.Win32Exception)
            {
                error = "No se puede utilizar " + stage + ". Esta aplicación portable necesita permisos de escritura en su propia carpeta para guardar configuración y logs. " +
                    "Mueve la aplicación a una carpeta local escribible y vuelve a abrirla. No se utilizará otra ubicación ni se solicitarán permisos de administrador.";
                return false;
            }
            finally { foreach (var guard in guards) guard.Dispose(); }
        }
        private static void ValidateConfigFile(string path, System.Collections.Generic.IList<Microsoft.Win32.SafeHandles.SafeFileHandle> guards)
        {
            try { guards.Add(PortablePathValidation.Open(path, false, true)); }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2) { } // Missing file is safe; dangling links are opened without following and rejected.
        }
        private void Probe(string directory)
        {
            string probe = Path.Combine(directory, ".write-check-" + Guid.NewGuid().ToString("N"));
            bool owned = false;
            try
            {
                using (var stream = createProbe(probe)) { owned = true; stream.WriteByte(0); stream.Flush(); }
                deleteProbe(probe);
                if (File.Exists(probe)) throw new IOException("No se pudo eliminar el archivo de prueba.");
                owned = false;
            }
            finally { if (owned) { try { File.Delete(probe); } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (System.Security.SecurityException) { } } }
        }

        public AppSettings LoadSettings()
        {
            if (!File.Exists(ConfigPath)) return new AppSettings();
            using (var stream = File.OpenRead(ConfigPath))
            {
                var settings = (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(stream) ?? new AppSettings();
                if (settings.Theme != "Light" && settings.Theme != "Dark" && settings.Theme != "System") settings.Theme = "Light";
                settings.Language = WinSereno.Localization.LocalizationService.Normalize(settings.Language);
                return settings;
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            var temporary = ConfigPath + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write))
                {
                    new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(stream, settings);
                    stream.Flush(true);
                }
                if (File.Exists(ConfigPath)) File.Replace(temporary, ConfigPath, null);
                else File.Move(temporary, ConfigPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
