using WinSereno.Models;

namespace WinSereno.ViewModels
{
    public sealed class TaskConfirmationViewModel
    {
        public MaintenanceTask Task { get; }
        public TaskConfirmationViewModel(MaintenanceTask task) { Task = task; }
        public string ConfirmationWarning => Task.ConfirmationWarning;
        public bool HasConfirmationWarning => !string.IsNullOrWhiteSpace(ConfirmationWarning);
        public string ImpactLabel
        {
            get
            {
                switch (Task.ImpactLevel)
                {
                    case ImpactLevel.Maintenance: return WinSereno.Localization.LocalizationService.Source("Text.Maintenance");
                    case ImpactLevel.Configuration: return WinSereno.Localization.LocalizationService.Source("Text.Configuration");
                    case ImpactLevel.Repair: return WinSereno.Localization.LocalizationService.Source("Text.Repair");
                    default: return WinSereno.Localization.LocalizationService.Source("Text.Information");
                }
            }
        }
        public string ElevationLabel => (Task.Id == WinSereno.Services.ElevatedTaskCatalog.FlushDnsId || Task.Id == WinSereno.Services.ElevatedTaskCatalog.RenewDhcpId) ? WinSereno.Localization.LocalizationService.Source("Text.OnlyIfWindowsDeniesPermissionFirstAttemptedWithout") : Task.RequiresElevation ? WinSereno.Localization.LocalizationService.Source("Text.Yes") : WinSereno.Localization.LocalizationService.Source("Text.No");
        public string RestartLabel => Task.MayRequireRestart ? WinSereno.Localization.LocalizationService.Source("Text.MayBeNeededNeverRestartsAutomatically") : WinSereno.Localization.LocalizationService.Source("Text.No");
        public string CancellationLabel => Task.CanBeCancelled ? WinSereno.Localization.LocalizationService.Source("Text.Yes") : WinSereno.Localization.LocalizationService.Source("Text.No");
        public string ExactCommand => (Task.Id == WinSereno.Services.ElevatedTaskCatalog.CompleteId || Task.Id == WinSereno.Services.ElevatedTaskCatalog.RenewDhcpId || Task.Id == WinSereno.Services.ElevatedTaskCatalog.RestartAdapterId) ? Task.Command : Task.Command + " " + Task.Arguments;
        public string ExecutionNotice => Task.Id == WinSereno.Services.CleanupBatchExecutor.TaskId ? (Task.RequiresElevation ? WinSereno.Localization.LocalizationService.Source("Text.PermissionWillBeRequestedBeforeAnyFilesAre") : WinSereno.Localization.LocalizationService.Source("Text.FilesInUseOrProtectedFilesWillBe")) : Task.Id == WinSereno.Services.RecycleBinCleanupService.TaskId ? WinSereno.Localization.LocalizationService.Source("Text.RunEmptiesTheCurrentUserSEntireRecycle") : Task.Id == WinSereno.Services.ThumbnailsCleanupService.TaskId ? WinSereno.Localization.LocalizationService.Source("Text.RunDeletesOnlyEligibleThumbcacheDbFilesNo") : Task.Id == WinSereno.Services.UserTempCleanupService.TaskId ? WinSereno.Localization.LocalizationService.Source("Text.RunDeletesOnlyEligibleUserTemporaryFilesNo") : Task.IsMock ? WinSereno.Localization.LocalizationService.Source("Text.RunStartsAnInternalSimulationOnly") : Task.Id == WinSereno.Services.ElevatedTaskCatalog.RenewDhcpId ? WinSereno.Localization.LocalizationService.Source("Text.RunReleasesAndRenewsDhcpOnTheListed") : Task.Id == WinSereno.Services.ElevatedTaskCatalog.FlushDnsId ? WinSereno.Localization.LocalizationService.Source("Text.RunFlushesTheDnsCacheUacIsRequested") : WinSereno.Localization.LocalizationService.Source("Text.RunRequestsUacForThisTaskTheMain");
    }
}
