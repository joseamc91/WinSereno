using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        public string[] ThemeChoices { get; } = { WinSereno.Localization.LocalizationService.Source("Text.Light"), WinSereno.Localization.LocalizationService.Source("Text.Dark"), WinSereno.Localization.LocalizationService.Source("Text.System") };
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
                Raise(nameof(SelectedMainNavigation)); Raise(nameof(SelectedSettingsNavigation));
                Raise(nameof(CurrentSection)); Raise(nameof(CurrentSectionCode)); Raise(nameof(PageTitle)); Raise(nameof(PageDescription)); Raise(nameof(PageNotice)); Raise(nameof(HasPageNotice));
                if (value.Section == NavigationSection.Network) _ = Network.RefreshAsync();
            }
        }
        // Each selector sees only its own group; null deselections never replace the active page.
        public NavigationItem SelectedMainNavigation
        {
            get => MainNavigation.Contains(SelectedNavigation) ? SelectedNavigation : null;
            set { if (value != null && MainNavigation.Contains(value)) SelectedNavigation = value; }
        }
        public NavigationItem SelectedSettingsNavigation
        {
            get => SettingsNavigation.Contains(SelectedNavigation) ? SelectedNavigation : null;
            set { if (value != null && SettingsNavigation.Contains(value)) SelectedNavigation = value; }
        }
        public NavigationSection CurrentSection => SelectedNavigation.Section;
        public string CurrentSectionCode => CurrentSection.ToString();
        public NavigationSection DiagnosisNavigationTarget => NavigationSection.Diagnosis;
        public NavigationSection RepairNavigationTarget => NavigationSection.Repair;
        public string PageTitle => CurrentSection == NavigationSection.Activity ? WinSereno.Localization.LocalizationService.Source("Text.ActionHistory") : SelectedNavigation.Label;
        public string ProductVersion => ProductInformation.DisplayVersion;
        public bool HasPageNotice => !string.IsNullOrWhiteSpace(PageNotice);
        public string PageNotice => "";
        public string PageDescription
        {
            get
            {
                switch (CurrentSection)
                {
                    case NavigationSection.Activity: return WinSereno.Localization.LocalizationService.Source("Text.ViewActionsPerformedDuringThisSessionTxtLogs");
                    case NavigationSection.Diagnosis: return WinSereno.Localization.LocalizationService.Source("Text.AnalyzesTheGeneralConditionOfYourPcWithout");
                    case NavigationSection.Repair: return WinSereno.Localization.LocalizationService.Source("Text.ChecksAndRepairsWindowsComponentsThroughExplicitActions");
                    case NavigationSection.Network: return WinSereno.Localization.LocalizationService.Source("Text.ViewAndRefreshNetworkStatusToolsRequestConfirmation");
                    case NavigationSection.Cleanup: return WinSereno.Localization.LocalizationService.Source("Text.AnalyzeRecoverableSpaceAndChooseWhichCategoriesTo");
                    case NavigationSection.Settings: return WinSereno.Localization.LocalizationService.Source("Text.CustomizeTheAppearanceAndViewWinserenoSettings");
                    default: return WinSereno.Localization.LocalizationService.Source("Text.SystemInformationRetrievedDirectlyFromWindows");
                }
            }
        }
        public void Navigate(NavigationSection section)
        {
            foreach (var item in Navigation) if (item.Section == section) { SelectedNavigation = item; if (section == NavigationSection.Repair) RepairNavigationRequested?.Invoke(this, EventArgs.Empty); return; }
        }
        public event EventHandler RepairNavigationRequested;
        public System.Collections.Generic.IReadOnlyList<WinSereno.Localization.LanguageChoice> LanguageChoices => WinSereno.Localization.LocalizationService.Languages;
        public WinSereno.Localization.LanguageChoice SelectedLanguage
        {
            get => LanguageChoices.First(l => l.Code == WinSereno.Localization.LocalizationService.Normalize(settings.Language));
            set
            {
                if (value == null || value.Code == settings.Language) return;
                settings.Language = WinSereno.Localization.LocalizationService.Normalize(value.Code);
                WinSereno.Localization.LocalizationService.Current.Apply(settings.Language);
                Raise(nameof(SelectedLanguage));
                try { storage.SaveSettings(settings); }
                catch (Exception ex) { dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.TheLanguageCouldNotBeSaved") + ex.Message); }
            }
        }
        public string SelectedTheme
        {
            get => settings.Theme == "System" ? WinSereno.Localization.LocalizationService.Source("Text.System") : settings.Theme == "Dark" ? WinSereno.Localization.LocalizationService.Source("Text.Dark") : WinSereno.Localization.LocalizationService.Source("Text.Light");
            set
            {
                var theme = value == WinSereno.Localization.LocalizationService.Source("Text.System") ? "System" : value == WinSereno.Localization.LocalizationService.Source("Text.Dark") ? "Dark" : "Light";
                if (settings.Theme == theme) { if (theme == "System") themes.Apply(theme); return; }
                settings.Theme = theme;
                themes.Apply(theme);
                Raise();
                try { storage.SaveSettings(settings); }
                catch (Exception ex) { dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.TheThemeHasBeenAppliedButCouldNot") + ex.Message); }
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
        public string TaskName => Diagnosis.IsRunning ? WinSereno.Localization.LocalizationService.Source("Text.Diagnostics") : Cleanup.IsRunning ? WinSereno.Localization.LocalizationService.Source("Text.CleanupAnalysis") : Progress.CurrentTask?.Name ?? WinSereno.Localization.LocalizationService.Source("Text.NoActiveTask");
        public string StatusText
        {
            get
            {
                if (Diagnosis.IsRunning) return Diagnosis.LiveProgress?.StepLabel ?? Diagnosis.Summary;
                if (Cleanup.IsRunning) return Cleanup.Summary;
                if (ShowDiagnosticToastCounts) return Progress.Result.ExecutionStatus == ExecutionStatus.Cancelled ? WinSereno.Localization.LocalizationService.Source("Text.DiagnosticsCancelled540") :
                    Progress.Result.ExecutionStatus == ExecutionStatus.Failed ? WinSereno.Localization.LocalizationService.Source("Text.DiagnosticsCouldNotBeCompletedResultsObtainedSo") : WinSereno.Localization.LocalizationService.Source("Text.DiagnosticsCompleted");
                if (Progress.State == RunnerState.Running) return Progress.StepLabel ?? (Progress.CurrentTask?.IsMock == true ? WinSereno.Localization.LocalizationService.Source("Text.RunningSimulation542") : WinSereno.Localization.LocalizationService.Source("Text.Running882") + TaskName + "...");
                if (Progress.State == RunnerState.Cancelling) return WinSereno.Localization.LocalizationService.Source("Text.CancellingSimulation");
                if (Progress.Result != null)
                    return Progress.Result.UserSummary;
                return WinSereno.Localization.LocalizationService.Source("Text.NoTaskIsRunning");
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
            AddNavigation(NavigationSection.Home, WinSereno.Localization.LocalizationService.Source("Text.Home")); AddNavigation(NavigationSection.Diagnosis, WinSereno.Localization.LocalizationService.Source("Text.Diagnostics"));
            AddNavigation(NavigationSection.Repair, WinSereno.Localization.LocalizationService.Source("Text.Repair")); AddNavigation(NavigationSection.Network, WinSereno.Localization.LocalizationService.Source("Text.Network"));
            AddNavigation(NavigationSection.Cleanup, WinSereno.Localization.LocalizationService.Source("Text.Cleanup")); AddNavigation(NavigationSection.Activity, WinSereno.Localization.LocalizationService.Source("Text.Activity"));
            AddNavigation(NavigationSection.Settings, WinSereno.Localization.LocalizationService.Source("Text.Settings"));
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
            RealRepairTasks.Add(new RepairTaskViewModel(ElevatedTaskCatalog.CheckHealthId, WinSereno.Localization.LocalizationService.Source("Text.DismCheckhealthQuickCheckOfTheRecordedState"), integrity));
            RealRepairTasks.Add(new RepairTaskViewModel(ElevatedTaskCatalog.ScanHealthId, "DISM ScanHealth", integrity));
            RealRepairTasks.Add(new RepairTaskViewModel(ElevatedTaskCatalog.RestoreHealthId, "DISM RestoreHealth", integrity));
            RealRepairTasks.Add(new RepairTaskViewModel(ElevatedTaskCatalog.SfcId, WinSereno.Localization.LocalizationService.Source("Text.SfcScannow"), integrity));
            RealRepairTasks.Add(new RepairTaskViewModel(ElevatedTaskCatalog.ComponentCleanupId, "DISM StartComponentCleanup · Mantenimiento", integrity));
            RealRepairTasks.Add(new RepairTaskViewModel(ElevatedTaskCatalog.ChkdskId, WinSereno.Localization.LocalizationService.Source("Text.ChkdskReadOnly"), integrity));
            RealRepairTasks.Add(new RepairTaskViewModel(ElevatedTaskCatalog.CompleteId, WinSereno.Localization.LocalizationService.Source("Text.SequenceConditionalRestorehealthOneUacRequest"), integrity));
            CancelTaskCommand = new RelayCommand(p => { if (Diagnosis.IsRunning) Diagnosis.CancelCommand.Execute(null); else if (Cleanup.IsRunning) Operations.RequestCancellation(); else Runner.RequestCancellation(); },
                p => Diagnosis.IsRunning ? Diagnosis.CancelCommand.CanExecute(null) : Cleanup.IsRunning ? Operations.CanBeCancelled : Runner.IsActive && Runner.Current.CurrentTask.CanBeCancelled);
            DetailsCommand = new RelayCommand(p => dialogs.ShowOutput(CreateToastDetails()), p => HasTask);
            HistoryDetailsCommand = new RelayCommand(p => { if (p is ActionHistoryEntry entry) dialogs.ShowOutput(entry.CreateDetailsProgress()); });
            DismissTaskPanelCommand = new RelayCommand(p => { panel.Dismiss(Progress, Operations.IsActive); RefreshTaskPanel(); }, p => CanDismissTaskPanel);
            CheckUpdatesCommand = new RelayCommand(p => { }, p => false);
            OpenLogsCommand = new RelayCommand(p => OpenLogs());
            OpenApplicationFolderCommand = new RelayCommand(p => OpenShell(this.shell.OpenApplicationFolder, WinSereno.Localization.LocalizationService.Source("Text.TheWinserenoFolder")));
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
                    using (Operations.Begin(WinSereno.Localization.LocalizationService.Source("Text.PreliminaryRecycleBinQueryReadOnly"), false)) Cleanup.SetRecycleBinAnalysis(await recycleBin.QueryAsync());
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
            catch (Exception ex) { logger.Write("Error limpieza seleccionada: " + ex); dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.SelectedCleanupCouldNotBeCompletedViewThe")); }
        }
        private async Task EmptyRecycleBinAsync()
        {
            if (Operations.IsActive || !Cleanup.CanClean(CleanupCategory.RecycleBin)) return;
            try
            {
                CleanupCategoryResult current;
                using (Operations.Begin(WinSereno.Localization.LocalizationService.Source("Text.PreliminaryRecycleBinQueryReadOnly"), false)) current = await recycleBin.QueryAsync();
                Cleanup.SetRecycleBinAnalysis(current);
                if (!current.IsAvailable) { logger.Write(WinSereno.Localization.LocalizationService.Source("Text.RecycleBinCannotBeQueriedEmptyingIsNot")); dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.TheRecycleBinCouldNotBeCheckedIt")); return; }
                var task = RecycleBinCleanupService.Prepare(current);
                logger.Write("Solicitud Vaciar Papelera | Elementos=" + current.FileCount + " | Bytes=" + current.TotalBytes);
                if (!dialogs.ConfirmTask(task)) { logger.Write("Confirmación Vaciar Papelera cancelada por el usuario; sin vaciado ni UAC."); return; }
                if (Operations.IsActive) return;
                var result = await Runner.RunAsync(task);
                if (result.RecycleBinAnalysis != null) Cleanup.SetRecycleBinAnalysis(result.RecycleBinAnalysis);
            }
            catch (Exception ex) { logger.Write("Error Vaciar Papelera: " + ex); dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.TheRecycleBinCouldNotBeEmptiedView")); }
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
            catch (Exception ex) { logger.Write("Error limpieza miniaturas: " + ex); dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.ThumbnailCleanupCouldNotBeCompletedViewThe")); }
        }
        private async Task CleanWindowsTempAsync()
        {
            if (Operations.IsActive || !Cleanup.CanClean(CleanupCategory.WindowsTemporary)) return;
            var task = WindowsTempCleanupService.Prepare(Cleanup.ElevatedWindowsTempAnalysis);
            logger.Write("Solicitud limpieza de temporales Windows | TaskId=" + task.Id);
            if (!dialogs.ConfirmTask(task)) { logger.Write("Confirmación limpieza Windows cancelada; sin UAC ni borrado."); return; }
            if (Operations.IsActive) return;
            try { var result = await Runner.RunAsync(task); if (result.WindowsTempAnalysis != null) Cleanup.SetWindowsTempAnalysis(result.WindowsTempAnalysis); }
            catch (Exception ex) { logger.Write("Error limpieza temporales Windows: " + ex); dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.WindowsTemporaryFileCleanupCouldNotBeCompleted")); }
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
            catch (Exception ex) { logger.Write("Error limpieza TEMP: " + ex); dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.TemporaryFileCleanupCouldNotBeCompletedView")); }
        }
        private async Task ResetTcpIpAsync()
        {
            if (Operations.IsActive) return;
            try
            {
                TcpIpResetSnapshot snapshot;
                using (var operation = Operations.Begin(WinSereno.Localization.LocalizationService.Source("Text.CheckIpv4Configuration"), true))
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
            catch (Exception ex) { logger.Write("Error Restablecer TCP/IP: " + ex); dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.TcpIpResetCouldNotBeCompletedView")); }
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
            catch (Exception ex) { logger.Write("Error Restablecer Winsock: " + ex); dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.WinsockResetCouldNotBeCompletedViewThe")); }
        }
        private async Task RestartAdapterAsync()
        {
            if (Operations.IsActive) return;
            try
            {
                AdapterRestartSelection selection;
                Network.SetRestartStatus(WinSereno.Localization.LocalizationService.Source("Text.CheckingConnectedPhysicalAdapters"));
                using (var operation = Operations.Begin(WinSereno.Localization.LocalizationService.Source("Text.SelectAdapterToRestart"), true))
                {
                    logger.Write("Solicitud reinicio de adaptador; selección de solo lectura, aún sin comandos.");
                    selection = await Task.Run(() => new AdapterRestartService(logger).ReadSelection());
                    if (operation.Token.IsCancellationRequested) { Network.SetRestartStatus("Selección cancelada; no se ejecutaron comandos."); return; }
                }
                if (!selection.ReadSucceeded || selection.Adapters.Count == 0)
                {
                    string message = selection.ReadSucceeded ? WinSereno.Localization.LocalizationService.Source("Text.NoEligibleActivePhysicalEthernetOrWiFi") : WinSereno.Localization.LocalizationService.Source("Text.AnActivePhysicalAdapterCouldNotBeReliably");
                    Network.SetRestartStatus(message); logger.Write(message); dialogs.ShowMessage(message); return;
                }
                var adapter = AdapterRestartPolicy.Select(selection.Adapters, dialogs.SelectRestartAdapter);
                if (adapter == null) { Network.SetRestartStatus("Selección cancelada; no se ejecutaron comandos."); return; }
                var task = AdapterRestartService.PrepareTask(adapter);
                if (Operations.IsActive) return;
                if (!dialogs.ConfirmTask(task)) { Network.SetRestartStatus(WinSereno.Localization.LocalizationService.Source("Text.ConfirmationCancelledNoUacWasRequestedAndThe")); logger.Write(WinSereno.Localization.LocalizationService.Source("Text.RestartConfirmationCancelled")); return; }
                if (Operations.IsActive) return;
                var result = await Runner.RunAsync(task);
                Network.SetRestartResult(result); await Network.RefreshAsync();
                if (result.ExecutionStatus == ExecutionStatus.Failed) dialogs.ShowMessage(result.UserSummary);
            }
            catch (Exception ex) { logger.Write("Error reinicio adaptador: " + ex); Network.SetRestartStatus(WinSereno.Localization.LocalizationService.Source("Text.RestartingCouldNotBeStartedCompletedViewThe")); dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.RestartingCouldNotBeStartedCompletedViewThe")); }
        }
        private async Task RenewDhcpAsync()
        {
            if (Operations.IsActive) return;
            try
            {
                DhcpRenewalPlan plan;
                Network.SetDhcpStatus(WinSereno.Localization.LocalizationService.Source("Text.CheckingActivePhysicalInterfacesWithDhcp"));
                using (var operation = Operations.Begin(WinSereno.Localization.LocalizationService.Source("Text.SelectDhcpInterfaces"), true))
                {
                    logger.Write("Solicitud Renovar dirección DHCP; detección de interfaces, aún sin comandos.");
                    plan = await Task.Run(() => new DhcpRenewalService(logger).ReadPlan());
                    if (operation.Token.IsCancellationRequested) { Network.SetDhcpStatus("Selección DHCP cancelada; no se ejecutó ningún comando."); return; }
                }
                if (!plan.ReadSucceeded || plan.Adapters.Count == 0)
                {
                    string message = plan.ReadSucceeded ? WinSereno.Localization.LocalizationService.Source("Text.NoActivePhysicalEthernetWiFiInterfaceHas") :
                        WinSereno.Localization.LocalizationService.Source("Text.DhcpInterfacesCouldNotBeReliablyVerifiedNo");
                    Network.SetDhcpStatus(message); logger.Write(message + "\n" + string.Join("\n", plan.Exclusions));
                    dialogs.ShowMessage(message); return;
                }
                var task = DhcpRenewalService.PrepareTask(plan);
                if (Operations.IsActive) { Network.SetDhcpStatus(WinSereno.Localization.LocalizationService.Source("Text.AnotherOperationIsActiveTrySelectingDhcpInterfaces")); return; }
                if (!dialogs.ConfirmTask(task)) { Network.SetDhcpStatus(WinSereno.Localization.LocalizationService.Source("Text.ConfirmationCancelledNoDhcpCommandsRan")); logger.Write("Confirmación DHCP cancelada; no se ejecutaron comandos."); return; }
                if (Operations.IsActive) return;
                var result = await Runner.RunAsync(task);
                Network.SetDhcpResult(result);
                await Network.RefreshAsync();
            }
            catch (Exception ex) { logger.Write("No se pudo iniciar renovación DHCP: " + ex); Network.SetDhcpStatus(WinSereno.Localization.LocalizationService.Source("Text.DhcpRenewalCouldNotBeCompletedViewThe")); dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.DhcpRenewalCouldNotBeCompletedViewThe")); }
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
            catch (Exception ex) { logger.Write("Error vaciar caché DNS: " + ex); dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.TheDnsCacheCouldNotBeFlushedView")); }
        }
        private async Task RunRepairAsync(string taskId)
        {
            if (Operations.IsActive) return;
            MaintenanceTask task;
            try { task = ElevatedTaskCatalog.Get(taskId); }
            catch (Exception ex) when (taskId == ElevatedTaskCatalog.ChkdskId) { logger.Write("CHKDSK: resolución del volumen falló | " + ex); dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.TheWindowsDriveCouldNotBeSafelyDetermined")); return; }
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
            catch (Exception ex) { logger.Write("Error en solicitud " + task.Id + ": " + ex); dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.TheToolCouldNotBeStartedViewThe")); }
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
                AddDiagnosticToastCount(WinSereno.Localization.LocalizationService.Source("Text.Error"), Diagnosis.ErrorCount);
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
                Name = cancelable ? WinSereno.Localization.LocalizationService.Source("Text.CancellableSampleTask") : WinSereno.Localization.LocalizationService.Source("Text.NonCancellableSampleTask"),
                ShortDescription = WinSereno.Localization.LocalizationService.Source("Text.A12SecondSimulationToReviewTheInterface"),
                DetailedDescription = WinSereno.Localization.LocalizationService.Source("Text.OnlyWaitsAndPublishesSampleTextDoesNot"),
                Category = TaskCategory.Diagnosis, ImpactLevel = ImpactLevel.Information, TaskType = TaskType.Internal,
                Command = WinSereno.Localization.LocalizationService.Source("Text.InternalSimulation"), Arguments = WinSereno.Localization.LocalizationService.Source("Text.NoSystemArguments"), RequiresElevation = false,
                CanBeCancelled = cancelable, MayRequireRestart = false, IsMock = true };
            if (!dialogs.ConfirmTask(task)) return;
            try { await Runner.RunAsync(task); }
            catch (Exception ex) { dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.TheSimulationCouldNotBeCompleted") + ex.Message); }
        }
        private void OpenLogs()
        {
            try
            {
                shell.OpenLogs();
            }
            catch (Exception ex) { dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.TheLogsFolderCouldNotBeOpened") + ex.Message); }
        }
        private void OpenShell(Action action, string name)
        { try { action(); } catch (Exception ex) { dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.CouldNotOpen") + name + ".\n" + ex.Message); } }
        private void ResetPreferences()
        {
            if ((dialogs as IPreferencesDialogs)?.ConfirmResetPreferences() != true) return;
            settings.Theme = new AppSettings().Theme;
            settings.Language = new AppSettings().Language;
            WinSereno.Localization.LocalizationService.Current.Apply(settings.Language); Raise(nameof(SelectedLanguage));
            themes.Apply(settings.Theme); Raise(nameof(SelectedTheme));
            try { storage.SaveSettings(settings); }
            catch (Exception ex) { dialogs.ShowMessage(WinSereno.Localization.LocalizationService.Source("Text.PreferencesHaveBeenAppliedButCouldNotBe") + ex.Message); }
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
