using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using WinSereno.Infrastructure;
using WinSereno.Models;
using WinSereno.Services;

namespace WinSereno.ViewModels
{
    public sealed class NavigationItem
    {
        public NavigationSection Section { get; set; }
        public string Label { get; set; }
        public string SectionCode => Section.ToString();
    }
    public sealed class ToolPlaceholder
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }
    public sealed class MainViewModel : ObservableObject
    {
        private readonly PortableStorage storage;
        private readonly ThemeService themes;
        private readonly IDialogService dialogs;
        private readonly ITcpIpResetPreflight tcpIpPreflight;
        private readonly AppSettings settings;
        private readonly IApplicationShell shell;
        public IMaintenanceTaskRunner Runner { get; }
        public ObservableCollection<NavigationItem> Navigation { get; } = new ObservableCollection<NavigationItem>();
        public ObservableCollection<NavigationItem> MainNavigation { get; } = new ObservableCollection<NavigationItem>();
        public ObservableCollection<NavigationItem> SettingsNavigation { get; } = new ObservableCollection<NavigationItem>();
        public HomeViewModel Home { get; }
        public DiagnosticViewModel Diagnosis { get; }
        public NetworkViewModel Network { get; }
        public CleanupViewModel Cleanup { get; }
        public OperationCoordinator Operations { get; }
        private readonly ActionHistoryService history = new ActionHistoryService();
        private readonly OperationPanelState panel = new OperationPanelState();
        private readonly Stopwatch analysisToastWatch = new Stopwatch();
        private readonly System.Windows.Threading.DispatcherTimer analysisToastTimer;
        public ObservableCollection<OperationToastCount> DiagnosticToastCounts { get; } = new ObservableCollection<OperationToastCount>();
        public ReadOnlyObservableCollection<ActionHistoryEntry> ActionHistory => history.Entries;
        public bool HasHistory => ActionHistory.Count != 0;
        public RelayCommand HistoryDetailsCommand { get; }
        public RelayCommand DismissTaskPanelCommand { get; }
        public ObservableCollection<ToolPlaceholder> RepairTools { get; } = new ObservableCollection<ToolPlaceholder>();
        public ObservableCollection<ToolPlaceholder> NetworkTools { get; } = new ObservableCollection<ToolPlaceholder>();
        public string[] ThemeChoices { get; } = { "Claro", "Oscuro", "Sistema" };
        public RelayCommand NavigateCommand { get; }
        public RelayCommand StartMockCommand { get; }
        public RelayCommand RunRepairCommand { get; }
        public RelayCommand FlushDnsCommand { get; }
        public RelayCommand ResetTcpIpCommand { get; }
        public RelayCommand CleanUserTempCommand { get; }
        public RelayCommand CleanWindowsTempCommand { get; }
        public RelayCommand CleanThumbnailsCommand { get; }
        public RelayCommand EmptyRecycleBinCommand { get; }
        public RelayCommand CleanSelectedCommand { get; }
        private readonly RecycleBinCleanupService recycleBin;
        public RelayCommand ResetWinsockCommand { get; }
        public RelayCommand RestartAdapterCommand { get; }
        public RelayCommand RenewDhcpCommand { get; }
        public ObservableCollection<RepairTaskViewModel> RealRepairTasks { get; } = new ObservableCollection<RepairTaskViewModel>();
        private readonly IntegritySessionState integrity;
        private readonly ISessionLogger logger;
        public RelayCommand CancelTaskCommand { get; }
        public RelayCommand DetailsCommand { get; }
        public RelayCommand OpenLogsCommand { get; }
        public RelayCommand OpenApplicationFolderCommand { get; }
        public RelayCommand OpenGitHubCommand { get; }
        public RelayCommand OpenReleasesCommand { get; }
        public RelayCommand ResetPreferencesCommand { get; }
        public RelayCommand CheckUpdatesCommand { get; }

        private NavigationItem selectedNavigation;
        public NavigationItem SelectedNavigation
        {
            get => selectedNavigation;
            set
            {
                if (value == null || !Set(ref selectedNavigation, value)) return;
                Raise(nameof(CurrentSection)); Raise(nameof(CurrentSectionCode)); Raise(nameof(PageTitle)); Raise(nameof(PageDescription)); Raise(nameof(PageNotice)); Raise(nameof(HasPageNotice));
                if (value.Section == NavigationSection.Network) _ = Network.RefreshAsync();
            }
        }
        public NavigationSection CurrentSection => SelectedNavigation.Section;
        public string CurrentSectionCode => CurrentSection.ToString();
        public NavigationSection DiagnosisNavigationTarget => NavigationSection.Diagnosis;
        public NavigationSection RepairNavigationTarget => NavigationSection.Repair;
        public string PageTitle => CurrentSection == NavigationSection.Activity ? "Registro de acciones" : SelectedNavigation.Label;
        public string ProductVersion => ProductInformation.DisplayVersion;
        public bool HasPageNotice => !string.IsNullOrWhiteSpace(PageNotice);
        public string PageNotice => "";
        public string PageDescription
        {
            get
            {
                switch (CurrentSection)
                {
                    case NavigationSection.Activity: return "Consulta las acciones realizadas durante esta sesión; los logs TXT conservan el registro persistente.";
                    case NavigationSection.Diagnosis: return "Analiza el estado general del PC sin realizar reparaciones. La integridad de Windows requiere permisos de administrador.";
                    case NavigationSection.Repair: return "Comprueba y repara componentes de Windows mediante acciones explícitas; las herramientas administrativas solicitan confirmación y permisos de administrador antes de ejecutarse.";
                    case NavigationSection.Network: return "Consulta y actualiza el estado de la red; las herramientas solicitan confirmación y permisos de administrador cuando corresponde.";
                    case NavigationSection.Cleanup: return "Analiza el espacio que puede recuperarse y elige qué categorías quieres limpiar.";
                    case NavigationSection.Settings: return "Personaliza la apariencia y consulta la configuración de WinSereno.";
                    default: return "Información del equipo obtenida directamente desde Windows.";
                }
            }
        }
        public void Navigate(NavigationSection section)
        {
            foreach (var item in Navigation) if (item.Section == section) { SelectedNavigation = item; if (section == NavigationSection.Repair) RepairNavigationRequested?.Invoke(this, EventArgs.Empty); return; }
        }
        public event EventHandler RepairNavigationRequested;
        public string SelectedTheme
        {
            get => settings.Theme == "System" ? "Sistema" : settings.Theme == "Dark" ? "Oscuro" : "Claro";
            set
            {
                var theme = value == "Sistema" ? "System" : value == "Oscuro" ? "Dark" : "Light";
                if (settings.Theme == theme) { if (theme == "System") themes.Apply(theme); return; }
                settings.Theme = theme;
                themes.Apply(theme);
                Raise();
                try { storage.SaveSettings(settings); }
                catch (Exception ex) { dialogs.ShowMessage("El tema se ha aplicado, pero no se pudo guardar en la carpeta de la aplicación.\n" + ex.Message); }
            }
        }
        private TaskProgress progress = new TaskProgress { State = RunnerState.Idle };
        public TaskProgress Progress { get => progress; private set => Set(ref progress, value); }
        public bool HasTask => Progress.CurrentTask != null || Diagnosis.IsRunning || Cleanup.IsRunning;
        public bool ShowTaskPanel => HasTask && (!panel.IsDismissed || Operations.IsActive);
        public bool CanDismissTaskPanel => ShowTaskPanel && panel.CanDismiss(Progress, Operations.IsActive);
        public bool IsActive => Operations.IsActive;
        public double? ToastPercentage => Diagnosis.IsRunning ? Diagnosis.LiveProgress?.Percentage : Cleanup.IsRunning ? null : Progress.Percentage;
        public bool HasPercentage => IsActive && ToastPercentage.HasValue;
        public bool CanCancelToast => IsActive && (Diagnosis.IsRunning ? Diagnosis.CancelCommand.CanExecute(null) : Cleanup.IsRunning ? Operations.CanBeCancelled : CancelTaskCommand.CanExecute(null));
        public bool ShowDiagnosticToastCounts => !IsActive && !Diagnosis.IsRunning && !Cleanup.IsRunning && Progress.State == RunnerState.Completed && Progress.Result != null && Progress.CurrentTask?.Id == "diagnosis.general";
        public string ToastLastRelevantLine => Diagnosis.IsRunning ? Diagnosis.LiveProgress?.LastRelevantLine : Cleanup.IsRunning ? null : Progress.LastRelevantLine;
        public string ElapsedText => (Diagnosis.IsRunning || Cleanup.IsRunning ? analysisToastWatch.Elapsed : Progress.Elapsed).ToString(@"hh\:mm\:ss");
        public string TaskName => Diagnosis.IsRunning ? "Diagnóstico" : Cleanup.IsRunning ? "Análisis de Limpieza" : Progress.CurrentTask?.Name ?? "Sin tarea activa";
        public string StatusText
        {
            get
            {
                if (Diagnosis.IsRunning) return Diagnosis.LiveProgress?.StepLabel ?? Diagnosis.Summary;
                if (Cleanup.IsRunning) return Cleanup.Summary;
                if (ShowDiagnosticToastCounts) return Progress.Result.ExecutionStatus == ExecutionStatus.Cancelled ? "Diagnóstico cancelado." :
                    Progress.Result.ExecutionStatus == ExecutionStatus.Failed ? "No se pudo completar el diagnóstico. Los resultados obtenidos se conservan." : "Diagnóstico finalizado.";
                if (Progress.State == RunnerState.Running) return Progress.StepLabel ?? (Progress.CurrentTask?.IsMock == true ? "Ejecutando... (simulación)" : "Ejecutando " + TaskName + "...");
                if (Progress.State == RunnerState.Cancelling) return "Cancelando simulación...";
                if (Progress.Result != null)
                    return Progress.Result.UserSummary;
                return "No hay ninguna tarea en ejecución.";
            }
        }

        public MainViewModel(PortableStorage storage, AppSettings settings, ThemeService themes, IDialogService dialogs, IMaintenanceTaskRunner runner, HomeViewModel home, DiagnosticViewModel diagnosis, OperationCoordinator operations, IntegritySessionState integrity, ISessionLogger logger, RecycleBinCleanupService recycleBin = null, ITcpIpResetPreflight tcpIpPreflight = null, CleanupViewModel cleanup = null, IApplicationShell shell = null)
        {
            this.shell = shell ?? new ApplicationShellService(storage);
            this.tcpIpPreflight = tcpIpPreflight ?? new TcpIpResetPreflightService(logger);
            this.recycleBin = recycleBin ?? new RecycleBinCleanupService(logger);
            this.integrity = integrity; this.logger = logger;
            Network = new NetworkViewModel(logger, operations);
            var cleanupRunner = runner as ICleanupAnalysisRunner;
            Cleanup = cleanup ?? new CleanupViewModel(new CleanupAnalysisService(logger), operations, logger,
                cleanupRunner == null ? (Func<OperationLease, Action<TaskProgress>, Task<MaintenanceTaskResult>>)null : cleanupRunner.RunCleanupAnalysisAsync);
            this.storage = storage; this.settings = settings; this.themes = themes; this.dialogs = dialogs; Runner = runner; Home = home; Diagnosis = diagnosis; Operations = operations;
            AddNavigation(NavigationSection.Home, "Inicio"); AddNavigation(NavigationSection.Diagnosis, "Diagnóstico");
            AddNavigation(NavigationSection.Repair, "Reparación"); AddNavigation(NavigationSection.Network, "Red");
            AddNavigation(NavigationSection.Cleanup, "Limpieza"); AddNavigation(NavigationSection.Activity, "Actividad");
            AddNavigation(NavigationSection.Settings, "Ajustes");
            selectedNavigation = Navigation[0];
            NavigateCommand = new RelayCommand(p => { if (p is NavigationSection destination) Navigate(destination); });
            StartMockCommand = new RelayCommand(async p => await StartMockAsync(p as string != "NonCancelable"), p => !Operations.IsActive);
            RunRepairCommand = new RelayCommand(async p => await RunRepairAsync(p as string), p => !Operations.IsActive && ElevatedTaskCatalog.IsAllowed(p as string));
            CleanSelectedCommand = new RelayCommand(async p => await CleanSelectedAsync(), p => !Operations.IsActive && Cleanup.CanCleanSelected);
            EmptyRecycleBinCommand = new RelayCommand(async p => await EmptyRecycleBinAsync(), p => !Operations.IsActive && Cleanup.CanClean(CleanupCategory.RecycleBin));
            CleanThumbnailsCommand = new RelayCommand(async p => await CleanThumbnailsAsync(), p => !Operations.IsActive && Cleanup.CanClean(CleanupCategory.ThumbnailCache));
            CleanWindowsTempCommand = new RelayCommand(async p => await CleanWindowsTempAsync(), p => !Operations.IsActive && Cleanup.CanClean(CleanupCategory.WindowsTemporary));
            CleanUserTempCommand = new RelayCommand(async p => await CleanUserTempAsync(), p => !Operations.IsActive && Cleanup.CanClean(CleanupCategory.UserTemporary));
            ResetTcpIpCommand = new RelayCommand(async p => await ResetTcpIpAsync(), p => !Operations.IsActive);
            ResetWinsockCommand = new RelayCommand(async p => await ResetWinsockAsync(), p => !Operations.IsActive);
            RestartAdapterCommand = new RelayCommand(async p => await RestartAdapterAsync(), p => !Operations.IsActive);
            RenewDhcpCommand = new RelayCommand(async p => await RenewDhcpAsync(), p => !Operations.IsActive);
            FlushDnsCommand = new RelayCommand(async p => await FlushDnsAsync(), p => !Operations.IsActive);
            RealRepairTasks.Add(new RepairTaskViewModel(ElevatedTaskCatalog.CheckHealthId, "DISM CheckHealth · Comprobación rápida del estado registrado", integrity));
            RealRepairTasks.Add(new RepairTaskViewModel(ElevatedTaskCatalog.ScanHealthId, "DISM ScanHealth", integrity));
            RealRepairTasks.Add(new RepairTaskViewModel(ElevatedTaskCatalog.RestoreHealthId, "DISM RestoreHealth", integrity));
            RealRepairTasks.Add(new RepairTaskViewModel(ElevatedTaskCatalog.SfcId, "SFC /scannow", integrity));
            RealRepairTasks.Add(new RepairTaskViewModel(ElevatedTaskCatalog.ComponentCleanupId, "DISM StartComponentCleanup · Mantenimiento", integrity));
            RealRepairTasks.Add(new RepairTaskViewModel(ElevatedTaskCatalog.ChkdskId, "CHKDSK · Solo lectura", integrity));
            RealRepairTasks.Add(new RepairTaskViewModel(ElevatedTaskCatalog.CompleteId, "Secuencia · RestoreHealth condicional · Un único UAC", integrity));
            CancelTaskCommand = new RelayCommand(p => { if (Diagnosis.IsRunning) Diagnosis.CancelCommand.Execute(null); else if (Cleanup.IsRunning) Operations.RequestCancellation(); else Runner.RequestCancellation(); },
                p => Diagnosis.IsRunning ? Diagnosis.CancelCommand.CanExecute(null) : Cleanup.IsRunning ? Operations.CanBeCancelled : Runner.IsActive && Runner.Current.CurrentTask.CanBeCancelled);
            DetailsCommand = new RelayCommand(p => dialogs.ShowOutput(CreateToastDetails()), p => HasTask);
            HistoryDetailsCommand = new RelayCommand(p => { if (p is ActionHistoryEntry entry) dialogs.ShowOutput(entry.CreateDetailsProgress()); });
            DismissTaskPanelCommand = new RelayCommand(p => { panel.Dismiss(Progress, Operations.IsActive); RefreshTaskPanel(); }, p => CanDismissTaskPanel);
            CheckUpdatesCommand = new RelayCommand(p => { }, p => false);
            OpenLogsCommand = new RelayCommand(p => OpenLogs());
            OpenApplicationFolderCommand = new RelayCommand(p => OpenShell(this.shell.OpenApplicationFolder, "la carpeta de WinSereno"));
            OpenGitHubCommand = new RelayCommand(p => OpenShell(this.shell.OpenGitHub, "GitHub"));
            OpenReleasesCommand = new RelayCommand(p => OpenShell(this.shell.OpenReleases, "Releases"));
            ResetPreferencesCommand = new RelayCommand(p => ResetPreferences());
            Runner.ProgressChanged += OnProgressChanged;
            Diagnosis.Completed += OnProgressChanged;
            Cleanup.Completed += OnProgressChanged;
            Operations.Changed += (s, e) => { StartMockCommand.Refresh(); RunRepairCommand.Refresh(); FlushDnsCommand.Refresh(); RenewDhcpCommand.Refresh(); RestartAdapterCommand.Refresh(); ResetWinsockCommand.Refresh(); ResetTcpIpCommand.Refresh(); RefreshCleanupCommands(); Raise(nameof(IsActive)); RefreshToastPresentation(); };
            analysisToastTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            analysisToastTimer.Tick += (s, e) => RefreshToastPresentation();
            Cleanup.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(CleanupViewModel.CanCleanSelected)) RefreshCleanupCommands(); if (e.PropertyName == nameof(CleanupViewModel.IsRunning)) ObserveAnalysisPresentation(); if (e.PropertyName == nameof(CleanupViewModel.Summary)) RefreshToastPresentation(); };
            Diagnosis.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(DiagnosticViewModel.IsRunning)) ObserveAnalysisPresentation(); else RefreshToastPresentation(); };
        }
        private async Task CleanSelectedAsync()
        {
            var selection = Cleanup.SelectedCategories;
            if (Operations.IsActive || !Cleanup.CanCleanSelected) return;
            try
            {
                if (selection.HasFlag(CleanupSelection.RecycleBin)) {
                    using (Operations.Begin("Consulta previa de Papelera (solo lectura)", false)) Cleanup.SetRecycleBinAnalysis(await recycleBin.QueryAsync());
                }
                var estimates = new System.Collections.Generic.List<CleanupCategoryResult>(); foreach (var row in Cleanup.Categories) estimates.Add(row.Result);
                var task = CleanupBatchExecutor.Prepare(selection, estimates);
                if (!dialogs.ConfirmTask(task)) { logger.Write("Confirmación Limpieza seleccionada cancelada; ningún borrado ni UAC."); return; }
                if (Operations.IsActive) return;
                var result = await Runner.RunAsync(task);
                if (result.CleanupBatch != null) foreach (var step in result.CleanupBatch.Steps) if (step.Analysis != null) {
                    if (step.Category == CleanupCategory.WindowsTemporary) Cleanup.SetWindowsTempAnalysis(step.Analysis);
                    else Cleanup.SetCategoryAnalysis(step.Analysis);
                }
            }
            catch (Exception ex) { logger.Write("Error limpieza seleccionada: " + ex); dialogs.ShowMessage("No se pudo completar la limpieza seleccionada. Consulta el log."); }
        }
        private async Task EmptyRecycleBinAsync()
        {
            if (Operations.IsActive || !Cleanup.CanClean(CleanupCategory.RecycleBin)) return;
            try
            {
                CleanupCategoryResult current;
                using (Operations.Begin("Consulta previa de Papelera (solo lectura)", false)) current = await recycleBin.QueryAsync();
                Cleanup.SetRecycleBinAnalysis(current);
                if (!current.IsAvailable) { logger.Write("Papelera no consultable; no se ofrece vaciado."); dialogs.ShowMessage("No se pudo consultar la Papelera. No se vaciará."); return; }
                var task = RecycleBinCleanupService.Prepare(current);
                logger.Write("Solicitud Vaciar Papelera | Elementos=" + current.FileCount + " | Bytes=" + current.TotalBytes);
                if (!dialogs.ConfirmTask(task)) { logger.Write("Confirmación Vaciar Papelera cancelada por el usuario; sin vaciado ni UAC."); return; }
                if (Operations.IsActive) return;
                var result = await Runner.RunAsync(task);
                if (result.RecycleBinAnalysis != null) Cleanup.SetRecycleBinAnalysis(result.RecycleBinAnalysis);
            }
            catch (Exception ex) { logger.Write("Error Vaciar Papelera: " + ex); dialogs.ShowMessage("No se pudo completar el vaciado de Papelera. Consulta el log."); }
        }
        private async Task CleanThumbnailsAsync()
        {
            if (Operations.IsActive || !Cleanup.CanClean(CleanupCategory.ThumbnailCache)) return;
            try
            {
                var task = ThumbnailsCleanupService.Prepare(Cleanup.ThumbnailAnalysis);
                logger.Write("Solicitud limpieza miniaturas | TaskId=" + task.Id);
                if (!dialogs.ConfirmTask(task)) { logger.Write("Confirmación limpieza miniaturas cancelada; sin borrado ni UAC."); return; }
                if (Operations.IsActive) return;
                var result = await Runner.RunAsync(task);
                if (result.ThumbnailAnalysis != null) Cleanup.SetThumbnailAnalysis(result.ThumbnailAnalysis);
            }
            catch (Exception ex) { logger.Write("Error limpieza miniaturas: " + ex); dialogs.ShowMessage("No se pudo completar la limpieza de miniaturas. Consulta el log."); }
        }
        private async Task CleanWindowsTempAsync()
        {
            if (Operations.IsActive || !Cleanup.CanClean(CleanupCategory.WindowsTemporary)) return;
            var task = WindowsTempCleanupService.Prepare(Cleanup.ElevatedWindowsTempAnalysis);
            logger.Write("Solicitud limpieza de temporales Windows | TaskId=" + task.Id);
            if (!dialogs.ConfirmTask(task)) { logger.Write("Confirmación limpieza Windows cancelada; sin UAC ni borrado."); return; }
            if (Operations.IsActive) return;
            try { var result = await Runner.RunAsync(task); if (result.WindowsTempAnalysis != null) Cleanup.SetWindowsTempAnalysis(result.WindowsTempAnalysis); }
            catch (Exception ex) { logger.Write("Error limpieza temporales Windows: " + ex); dialogs.ShowMessage("No se pudo completar la limpieza de temporales de Windows. Consulta el log."); }
        }
        private async Task CleanUserTempAsync()
        {
            if (Operations.IsActive || !Cleanup.CanClean(CleanupCategory.UserTemporary)) return;
            try
            {
                var task = UserTempCleanupService.Prepare(Cleanup.LastResult);
                logger.Write("Solicitud limpieza TEMP | TaskId=" + task.Id);
                if (!dialogs.ConfirmTask(task)) { logger.Write("Confirmación limpieza TEMP cancelada; sin borrado."); return; }
                if (Operations.IsActive) return;
                await Runner.RunAsync(task);
                await Cleanup.AnalyzeAsync(false);
            }
            catch (Exception ex) { logger.Write("Error limpieza TEMP: " + ex); dialogs.ShowMessage("No se pudo completar la limpieza de temporales. Consulta el log."); }
        }
        private async Task ResetTcpIpAsync()
        {
            if (Operations.IsActive) return;
            try
            {
                TcpIpResetSnapshot snapshot;
                using (var operation = Operations.Begin("Comprobar configuración IPv4", true))
                {
                    logger.Write("Preflight TCP/IP iniciado; solo lectura, sin comandos ni elevación.");
                    snapshot = await Task.Run(() => tcpIpPreflight.Read());
                    if (operation.Token.IsCancellationRequested) { logger.Write("Preflight TCP/IP cancelado; sin UAC ni comandos."); return; }
                }
                logger.Write("Preflight TCP/IP | Interfaces revisadas=" + snapshot.Interfaces.Count +
                    " | DHCP=" + System.Linq.Enumerable.Count(snapshot.Interfaces, a => a.Mode == Ipv4ConfigurationMode.Dhcp) +
                    " | Manual=" + System.Linq.Enumerable.Count(snapshot.Interfaces, a => a.Mode == Ipv4ConfigurationMode.Manual) +
                    " | Indeterminado=" + System.Linq.Enumerable.Count(snapshot.Interfaces, a => a.Mode == Ipv4ConfigurationMode.Unknown) +
                    " | Lectura completa=" + snapshot.ReadComplete + " | Segunda advertencia requerida=" + snapshot.RequiresWarning);
                var task = ElevatedTaskCatalog.Get(ElevatedTaskCatalog.ResetTcpIpId);
                var session = RemoteSessionService.Read(); task.ConfirmationWarning = RemoteSessionService.GetWarning(session);
                logger.Write("Solicitud Restablecer TCP/IP | TaskId=" + task.Id + " | Sesión remota=" + session.IsRemote + " | Detección fiable=" + session.IsKnown + " | API=" + session.Source);
                if (Operations.IsActive) return;
                if (!dialogs.ConfirmTask(task)) { logger.Write("Confirmación TCP/IP cancelada; sin elevación ni comandos."); return; }
                bool warningAccepted = false;
                if (snapshot.RequiresWarning)
                {
                    var warningDialogs = dialogs as ITcpIpResetDialogs;
                    warningAccepted = warningDialogs != null && warningDialogs.ConfirmTcpIpReset(snapshot);
                    logger.Write("Segunda advertencia TCP/IP " + (warningAccepted ? "aceptada" : "cancelada") + "; aún sin UAC ni comandos.");
                    if (!warningAccepted) return;
                }
                if (Operations.IsActive) return;
                task.TcpIpApproval = new TcpIpResetApproval(snapshot, warningAccepted);
                logger.Write("Confirmaciones TCP/IP completadas; se permite solicitar UAC y revalidar en el worker.");
                var result = await Runner.RunAsync(task); Network.SetTcpIpResult(result);
            }
            catch (Exception ex) { logger.Write("Error Restablecer TCP/IP: " + ex); dialogs.ShowMessage("No se pudo completar el restablecimiento TCP/IP. Consulta el log."); }
        }
        private async Task ResetWinsockAsync()
        {
            if (Operations.IsActive) return;
            var task = ElevatedTaskCatalog.Get(ElevatedTaskCatalog.ResetWinsockId);
            var session = RemoteSessionService.Read(); task.ConfirmationWarning = RemoteSessionService.GetWarning(session);
            logger.Write("Solicitud Restablecer Winsock | TaskId=" + task.Id + " | Sesión remota=" + session.IsRemote + " | Detección fiable=" + session.IsKnown + " | API=" + session.Source);
            if (!dialogs.ConfirmTask(task)) { logger.Write("Confirmación Winsock cancelada; no se solicitó elevación ni se ejecutó ningún comando."); return; }
            if (Operations.IsActive) return;
            try
            {
                var result = await Runner.RunAsync(task); Network.SetWinsockResult(result);
                if (result.ExecutionStatus != ExecutionStatus.Cancelled) await Network.RefreshAsync();
            }
            catch (Exception ex) { logger.Write("Error Restablecer Winsock: " + ex); dialogs.ShowMessage("No se pudo completar el restablecimiento Winsock. Consulta el log."); }
        }
        private async Task RestartAdapterAsync()
        {
            if (Operations.IsActive) return;
            try
            {
                AdapterRestartSelection selection;
                Network.SetRestartStatus("Comprobando adaptadores físicos conectados...");
                using (var operation = Operations.Begin("Seleccionar adaptador para reinicio", true))
                {
                    logger.Write("Solicitud reinicio de adaptador; selección de solo lectura, aún sin comandos.");
                    selection = await Task.Run(() => new AdapterRestartService(logger).ReadSelection());
                    if (operation.Token.IsCancellationRequested) { Network.SetRestartStatus("Selección cancelada; no se ejecutaron comandos."); return; }
                }
                if (!selection.ReadSucceeded || selection.Adapters.Count == 0)
                {
                    string message = selection.ReadSucceeded ? "No hay adaptadores físicos Ethernet o Wi-Fi activos elegibles para reiniciar." : "No se pudo verificar de forma fiable un adaptador físico activo.";
                    Network.SetRestartStatus(message); logger.Write(message); dialogs.ShowMessage(message); return;
                }
                var adapter = AdapterRestartPolicy.Select(selection.Adapters, dialogs.SelectRestartAdapter);
                if (adapter == null) { Network.SetRestartStatus("Selección cancelada; no se ejecutaron comandos."); return; }
                var task = AdapterRestartService.PrepareTask(adapter);
                if (Operations.IsActive) return;
                if (!dialogs.ConfirmTask(task)) { Network.SetRestartStatus("Confirmación cancelada; no se solicitó UAC ni se reinició el adaptador."); logger.Write("Confirmación reinicio cancelada."); return; }
                if (Operations.IsActive) return;
                var result = await Runner.RunAsync(task);
                Network.SetRestartResult(result); await Network.RefreshAsync();
                if (result.ExecutionStatus == ExecutionStatus.Failed) dialogs.ShowMessage(result.UserSummary);
            }
            catch (Exception ex) { logger.Write("Error reinicio adaptador: " + ex); Network.SetRestartStatus("No se pudo iniciar/completar el reinicio. Consulta el log."); dialogs.ShowMessage("No se pudo iniciar/completar el reinicio. Consulta el log."); }
        }
        private async Task RenewDhcpAsync()
        {
            if (Operations.IsActive) return;
            try
            {
                DhcpRenewalPlan plan;
                Network.SetDhcpStatus("Comprobando interfaces físicas activas con DHCP...");
                using (var operation = Operations.Begin("Seleccionar interfaces DHCP", true))
                {
                    logger.Write("Solicitud Renovar dirección DHCP; detección de interfaces, aún sin comandos.");
                    plan = await Task.Run(() => new DhcpRenewalService(logger).ReadPlan());
                    if (operation.Token.IsCancellationRequested) { Network.SetDhcpStatus("Selección DHCP cancelada; no se ejecutó ningún comando."); return; }
                }
                if (!plan.ReadSucceeded || plan.Adapters.Count == 0)
                {
                    string message = plan.ReadSucceeded ? "No hay ninguna interfaz física Ethernet/Wi-Fi activa con IPv4 configurado mediante DHCP. No se ejecutó ningún comando." :
                        "No se pudieron verificar de forma fiable las interfaces DHCP. No se ejecutó ningún comando.";
                    Network.SetDhcpStatus(message); logger.Write(message + "\n" + string.Join("\n", plan.Exclusions));
                    dialogs.ShowMessage(message); return;
                }
                var task = DhcpRenewalService.PrepareTask(plan);
                if (Operations.IsActive) { Network.SetDhcpStatus("Otra operación está activa; vuelve a intentar la selección DHCP."); return; }
                if (!dialogs.ConfirmTask(task)) { Network.SetDhcpStatus("Confirmación cancelada; no se ejecutó ningún comando DHCP."); logger.Write("Confirmación DHCP cancelada; no se ejecutaron comandos."); return; }
                if (Operations.IsActive) return;
                var result = await Runner.RunAsync(task);
                Network.SetDhcpResult(result);
                await Network.RefreshAsync();
            }
            catch (Exception ex) { logger.Write("No se pudo iniciar renovación DHCP: " + ex); Network.SetDhcpStatus("No se pudo completar la renovación DHCP. Consulta el log."); dialogs.ShowMessage("No se pudo completar la renovación DHCP. Consulta el log."); }
        }
        private async Task FlushDnsAsync()
        {
            if (Operations.IsActive) return;
            var task = ElevatedTaskCatalog.Get(ElevatedTaskCatalog.FlushDnsId);
            logger.Write("Solicitud Vaciar caché DNS | TaskId=" + task.Id);
            if (!dialogs.ConfirmTask(task)) { logger.Write("Confirmación cancelada; no se vació la caché DNS."); return; }
            if (Operations.IsActive) return;
            try
            {
                var result = await Runner.RunAsync(task);
                Network.SetFlushDnsResult(result);
                if (result.FindingStatus == FindingStatus.Completed) await Network.RefreshAsync();
            }
            catch (Exception ex) { logger.Write("Error vaciar caché DNS: " + ex); dialogs.ShowMessage("No se pudo completar el vaciado de caché DNS. Consulta el log."); }
        }
        private async Task RunRepairAsync(string taskId)
        {
            if (Operations.IsActive) return;
            MaintenanceTask task;
            try { task = ElevatedTaskCatalog.Get(taskId); }
            catch (Exception ex) when (taskId == ElevatedTaskCatalog.ChkdskId) { logger.Write("CHKDSK: resolución del volumen falló | " + ex); dialogs.ShowMessage("No se pudo determinar con seguridad la unidad de Windows. CHKDSK no se ejecutará."); return; }
            logger.Write("Solicitud administrativa | TaskId=" + task.Id);
            if (!dialogs.ConfirmTask(task)) { logger.Write("Confirmación cancelada; no se solicita elevación. TaskId=" + task.Id); return; }
            if (Operations.IsActive) return;
            try
            {
                var result = await Runner.RunAsync(task);
                // Cancelling UAC never ran a new check; preserve any previous actual integrity result.
                if (result.ExecutionStatus != ExecutionStatus.Cancelled) integrity.Update(task.Id, result);
                foreach (var step in result.SequenceSteps)
                    if (!step.WasSkipped && step.Result.ExecutionStatus != ExecutionStatus.Cancelled) integrity.Update(step.TaskId, step.Result);
            }
            catch (Exception ex) { logger.Write("Error en solicitud " + task.Id + ": " + ex); dialogs.ShowMessage("No se pudo iniciar la herramienta. Consulta el log para obtener más información."); }
        }
        private void OnProgressChanged(object sender, TaskProgress value)
        {
            panel.Observe(value);
            RecordHistory(sender, value);
            Progress = value;
            if (value.State == RunnerState.Completed && value.CurrentTask?.Id == "diagnosis.general")
            {
                DiagnosticToastCounts.Clear();
                AddDiagnosticToastCount("Healthy", Diagnosis.HealthyCount);
                AddDiagnosticToastCount("Attention", Diagnosis.AttentionCount);
                AddDiagnosticToastCount("Error", Diagnosis.ErrorCount);
                AddDiagnosticToastCount("NotChecked", Diagnosis.NotCheckedCount);
            }
            Raise(nameof(HasTask)); Raise(nameof(IsActive)); Raise(nameof(HasPercentage)); Raise(nameof(ElapsedText));
            Raise(nameof(TaskName)); Raise(nameof(StatusText));
            StartMockCommand.Refresh(); CancelTaskCommand.Refresh(); DetailsCommand.Refresh();
            RefreshTaskPanel();
            RefreshToastPresentation();
        }
        private void AddDiagnosticToastCount(string status, int count)
        { if (count > 0) DiagnosticToastCounts.Add(new OperationToastCount(status, count, DiagnosticToastCounts.Count == 0)); }
        private void ObserveAnalysisPresentation()
        {
            if (Diagnosis.IsRunning || Cleanup.IsRunning)
            {
                analysisToastWatch.Restart(); analysisToastTimer.Start();
                panel.Observe(new TaskProgress { State = RunnerState.Running });
            }
            else { analysisToastTimer.Stop(); analysisToastWatch.Stop(); }
            RefreshToastPresentation();
        }
        private void RefreshToastPresentation()
        {
            Raise(nameof(HasTask)); Raise(nameof(TaskName)); Raise(nameof(StatusText)); Raise(nameof(ElapsedText));
            Raise(nameof(HasPercentage)); Raise(nameof(ToastPercentage)); Raise(nameof(ToastLastRelevantLine));
            Raise(nameof(CanCancelToast)); Raise(nameof(ShowDiagnosticToastCounts));
            CancelTaskCommand.Refresh(); DetailsCommand.Refresh(); RefreshTaskPanel();
        }
        private void RefreshCleanupCommands()
        {
            CleanUserTempCommand.Refresh(); CleanWindowsTempCommand.Refresh(); CleanThumbnailsCommand.Refresh(); EmptyRecycleBinCommand.Refresh(); CleanSelectedCommand.Refresh();
        }
        private TaskProgress CreateToastDetails()
        {
            if (!Diagnosis.IsRunning && !Cleanup.IsRunning) return Progress;
            var text = new System.Text.StringBuilder();
            if (Diagnosis.IsRunning)
            {
                foreach (var result in Diagnosis.Results) text.AppendLine(result.Name + "\n" + new DiagnosticDetailsViewModel(result).Text + "\n");
                text.AppendLine(Diagnosis.LiveProgress?.StdOut);
            }
            else foreach (var row in Cleanup.Categories) text.AppendLine(row.Name + "\n" + row.Summary + "\n" + row.Details + "\n");
            return new TaskProgress { State = RunnerState.Running, CurrentTask = new MaintenanceTask { Name = TaskName },
                Elapsed = analysisToastWatch.Elapsed, StdOut = text.ToString(), StdErr = Diagnosis.IsRunning ? Diagnosis.LiveProgress?.StdErr : null };
        }
        private void RecordHistory(object sender, TaskProgress value)
        {
            history.Record(value); Raise(nameof(HasHistory));
        }
        private void RefreshTaskPanel()
        {
            Raise(nameof(ShowTaskPanel)); Raise(nameof(CanDismissTaskPanel)); DismissTaskPanelCommand.Refresh();
        }
        private async Task StartMockAsync(bool cancelable)
        {
            if (Operations.IsActive) return;
            var task = new MaintenanceTask { Id = cancelable ? "mock.cancelable" : "mock.non-cancelable",
                Name = cancelable ? "Tarea de ejemplo cancelable" : "Tarea de ejemplo no cancelable",
                ShortDescription = "Simulación de 12 segundos para revisar la interfaz y el cierre de la aplicación.",
                DetailedDescription = "Solo espera y publica texto de ejemplo. No inicia procesos, no diagnostica Windows y no modifica su configuración. El resultado ficticio demuestra que ExitCode 0 puede coexistir con RepairRequired.",
                Category = TaskCategory.Diagnosis, ImpactLevel = ImpactLevel.Information, TaskType = TaskType.Internal,
                Command = "(simulación interna)", Arguments = "(sin argumentos de sistema)", RequiresElevation = false,
                CanBeCancelled = cancelable, MayRequireRestart = false, IsMock = true };
            if (!dialogs.ConfirmTask(task)) return;
            try { await Runner.RunAsync(task); }
            catch (Exception ex) { dialogs.ShowMessage("No se pudo completar la simulación: " + ex.Message); }
        }
        private void OpenLogs()
        {
            try
            {
                shell.OpenLogs();
            }
            catch (Exception ex) { dialogs.ShowMessage("No se pudo abrir la carpeta de logs.\n" + ex.Message); }
        }
        private void OpenShell(Action action, string name)
        { try { action(); } catch (Exception ex) { dialogs.ShowMessage("No se pudo abrir " + name + ".\n" + ex.Message); } }
        private void ResetPreferences()
        {
            if ((dialogs as IPreferencesDialogs)?.ConfirmResetPreferences() != true) return;
            settings.Theme = new AppSettings().Theme;
            themes.Apply(settings.Theme); Raise(nameof(SelectedTheme));
            try { storage.SaveSettings(settings); }
            catch (Exception ex) { dialogs.ShowMessage("Las preferencias se han aplicado, pero no se pudieron guardar.\n" + ex.Message); }
        }
        private void AddNavigation(NavigationSection section, string label)
        {
            var item = new NavigationItem { Section = section, Label = label }; Navigation.Add(item);
            if (section == NavigationSection.Settings) SettingsNavigation.Add(item); else MainNavigation.Add(item);
        }
        private static void AddTools(ObservableCollection<ToolPlaceholder> items, string[] names, string description)
        { foreach (var name in names) items.Add(new ToolPlaceholder { Name = name, Description = description }); }
    }
}
