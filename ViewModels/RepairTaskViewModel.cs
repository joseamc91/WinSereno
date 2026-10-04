using WinSereno.Infrastructure;
using WinSereno.Models;
using WinSereno.Services;
namespace WinSereno.ViewModels
{
    public sealed class RepairTaskViewModel : ObservableObject
    {
        private readonly IntegritySessionState integrity;
        public MaintenanceTask Task { get; }
        public string TechnicalName { get; }
        public string DisplayTitle => Task.Id == ElevatedTaskCatalog.ChkdskId ? WinSereno.Localization.LocalizationService.Source("Text.CheckDisk") : Task.Name;
        public string DisplayTechnicalName
        {
            get
            {
                switch (Task.Id)
                {
                    case ElevatedTaskCatalog.CheckHealthId: return WinSereno.Localization.LocalizationService.Source("Text.DismCheckhealth");
                    case ElevatedTaskCatalog.ScanHealthId: return WinSereno.Localization.LocalizationService.Source("Text.DismScanhealth");
                    case ElevatedTaskCatalog.RestoreHealthId: return WinSereno.Localization.LocalizationService.Source("Text.DismRestorehealth");
                    case ElevatedTaskCatalog.SfcId: return WinSereno.Localization.LocalizationService.Source("Text.SfcScannow");
                    case ElevatedTaskCatalog.ComponentCleanupId: return WinSereno.Localization.LocalizationService.Source("Text.DismStartcomponentcleanup");
                    case ElevatedTaskCatalog.ChkdskId: return WinSereno.Localization.LocalizationService.Source("Text.Chkdsk");
                    default: return TechnicalName;
                }
            }
        }
        public string DisplayDescription
        {
            get
            {
                switch (Task.Id)
                {
                    case ElevatedTaskCatalog.CheckHealthId: return WinSereno.Localization.LocalizationService.Source("Text.QuicklyChecksWhetherWindowsHasRecordedComponentStore");
                    case ElevatedTaskCatalog.ScanHealthId: return WinSereno.Localization.LocalizationService.Source("Text.PerformsAnInDepthCorruptionScanWithoutMaking");
                    case ElevatedTaskCatalog.RestoreHealthId: return WinSereno.Localization.LocalizationService.Source("Text.ScansAndRepairsCorruptionInTheWindowsComponent");
                    case ElevatedTaskCatalog.SfcId: return WinSereno.Localization.LocalizationService.Source("Text.ChecksProtectedWindowsFilesAndRepairsDamagedFiles");
                    case ElevatedTaskCatalog.ComponentCleanupId: return WinSereno.Localization.LocalizationService.Source("Text.RemovesSupersededComponentVersionsThatWindowsNoLonger");
                    case ElevatedTaskCatalog.ChkdskId: return WinSereno.Localization.LocalizationService.Source("Text.ChecksTheFileSystemOnTheWindowsDrive");
                    default: return Task.ShortDescription;
                }
            }
        }
        public string ButtonLabel => Task.Id == ElevatedTaskCatalog.ComponentCleanupId ? WinSereno.Localization.LocalizationService.Source("Text.CleanUpComponents") : Task.Id == ElevatedTaskCatalog.CompleteId ? WinSereno.Localization.LocalizationService.Source("Text.StartFullRepair") : (Task.Id == ElevatedTaskCatalog.ChkdskId || Task.Id == ElevatedTaskCatalog.CheckHealthId) ? WinSereno.Localization.LocalizationService.Source("Text.CheckAction") : Task.Id == ElevatedTaskCatalog.ScanHealthId ? WinSereno.Localization.LocalizationService.Source("Text.Analyze") : Task.Id == ElevatedTaskCatalog.RestoreHealthId ? WinSereno.Localization.LocalizationService.Source("Text.RepairAction") : WinSereno.Localization.LocalizationService.Source("Text.CheckAndRepair");
        public RepairTaskViewModel(string id, string technicalName, IntegritySessionState integrity)
        { try { Task = ElevatedTaskCatalog.Get(id); }
            catch (System.InvalidOperationException) when (id == ElevatedTaskCatalog.ChkdskId) { Task = new MaintenanceTask { Id = id, Name = WinSereno.Localization.LocalizationService.Source("Text.CheckDiskChkdsk"), ShortDescription = WinSereno.Localization.LocalizationService.Source("Text.TheWindowsDriveCouldNotBeSafelyDetermined"), CanBeCancelled = false }; }
            TechnicalName = technicalName; this.integrity = integrity; integrity.Changed += (s, e) => Raise(nameof(ResultText)); }
        public string ResultText { get { var r = integrity.Get(Task.Id); return r == null ? WinSereno.Localization.LocalizationService.Source("Text.NoResultsInThisSession") : WinSereno.Localization.LocalizationService.Source("Text.LastResult") + (Task.Id == ElevatedTaskCatalog.ComponentCleanupId ? r.ExecutionStatus == ExecutionStatus.Cancelled ? WinSereno.Localization.LocalizationService.Source("Text.Cancelled744") : r.FindingStatus == FindingStatus.Completed ? WinSereno.Localization.LocalizationService.Source("Text.Completed") : r.FindingStatus == FindingStatus.RestartRequired ? WinSereno.Localization.LocalizationService.Source("Text.CompletedRestartRequired") : r.ExecutionStatus == ExecutionStatus.Failed ? WinSereno.Localization.LocalizationService.Source("Text.FailedResult") : WinSereno.Localization.LocalizationService.Source("Text.UnknownResult") : new DiagnosticResult { Status = Task.Id == ElevatedTaskCatalog.ChkdskId && r.FindingStatus == FindingStatus.Attention ? DiagnosticStatus.Attention : IntegritySessionState.Status(r) }.StatusLabel) + WinSereno.Localization.LocalizationService.Source("Text.Separator") + r.UserSummary + WinSereno.Localization.LocalizationService.Source("Text.Duration") + r.Duration.TotalSeconds.ToString("0.0") + " s"; } }
    }
}
