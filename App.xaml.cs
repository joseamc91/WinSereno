using System;
using System.Windows;
using WinSereno.Services;
using WinSereno.ViewModels;
using WinSereno.Views;

namespace WinSereno
{
    public partial class App : Application
    {
        private SessionLogger logger;
        private HomeViewModel home;
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (e.Args.Length != 0)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Shutdown(await ElevatedWorker.RunAsync(e.Args));
                return;
            }
            WinSereno.Localization.LocalizationPresentation.Initialize();
            var storage = new PortableStorage(System.IO.Path.GetDirectoryName(typeof(App).Assembly.Location));
            if (!storage.CheckWritable(out var error)) { WinSereno.Localization.LocalizedMessageBox.Show(error, "Ubicación no escribible"); Shutdown(); return; }
            try
            {
                logger = new SessionLogger(storage);
                logger.Write("Inicio de aplicación | WinSereno 0.1 | Fase 5 | Administrador=" +
                    new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator));
                AppSettings settings;
                try { settings = storage.LoadSettings(); }
                catch (Exception ex)
                {
                    logger.Write("Configuración no válida: " + ex.Message);
                    WinSereno.Localization.LocalizedMessageBox.Show("No se pudo leer config.json. Se usará el tema claro.\n" + ex.Message, "Configuración");
                    settings = new AppSettings();
                }
                WinSereno.Localization.LocalizationService.Current.Apply(settings.Language);
                var themes = new ThemeService();
                themes.Apply(settings.Theme);
                if (!System.IO.File.Exists(storage.ConfigPath)) storage.SaveSettings(settings);
                var operations = new OperationCoordinator();
                var runner = new MaintenanceTaskRunner(logger, operations);
                var dialogs = new DialogService();
                var information = new SystemInformationService(logger);
                home = new HomeViewModel(information, logger);
                var integrity = new IntegritySessionState();
                var diagnosis = new DiagnosticViewModel(new DiagnosticService(information, logger, integrity, runner.RunDiagnosticAsync), operations, logger, dialogs, integrity);
                var window = new MainWindow(new MainViewModel(storage, settings, themes, dialogs, runner, home, diagnosis, operations, integrity, logger), dialogs);
                MainWindow = window;
                window.Show();
            }
            catch (Exception ex)
            {
                WinSereno.Localization.LocalizedMessageBox.Show("No se pudo iniciar la aplicación portable.\n" + ex.Message, "WinSereno");
                Shutdown(1);
            }
        }
        protected override void OnExit(ExitEventArgs e)
        {
            home?.Stop();
            try { logger?.Write("Cierre de aplicación | Código=" + e.ApplicationExitCode); }
            finally { logger?.Dispose(); }
            base.OnExit(e);
        }
    }
}
