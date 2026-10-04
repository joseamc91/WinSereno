namespace WinSereno.Models
{
    public enum UserTempCleanupStatus { Completed, NothingToClean, Partial, Failed }
    public sealed class UserTempCleanupResult
    {
        public string TechnicalDetails { get; internal set; } = "";
        public System.TimeSpan Duration { get; internal set; }
        public UserTempCleanupStatus Status { get; internal set; }
        public long DeletedFiles { get; internal set; }
        public long RecoveredBytes { get; internal set; }
        public long DeletedDirectories { get; internal set; }
        public long ProtectedElements { get; internal set; }
        public long ReparsePoints { get; internal set; }
        public long InaccessibleElements { get; internal set; }
        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" }; double value = bytes; int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return value.ToString("0.#") + " " + units[unit];
        }
        public string Summary => (Status == UserTempCleanupStatus.Completed ? WinSereno.Localization.LocalizationService.Source("Text.Completed") : Status == UserTempCleanupStatus.NothingToClean ? WinSereno.Localization.LocalizationService.Source("Text.NothingToClean") : Status == UserTempCleanupStatus.Partial ? WinSereno.Localization.LocalizationService.Source("Text.PartiallyCompleted") : WinSereno.Localization.LocalizationService.Source("Text.OperationFailed")) + WinSereno.Localization.LocalizationService.Source("Text.Separator") + DeletedFiles + WinSereno.Localization.LocalizationService.Source("Text.FilesDeleted") +
            FormatBytes(RecoveredBytes) + " recuperados · " + DeletedDirectories +
            WinSereno.Localization.LocalizationService.Source("Text.EmptyFoldersDeleted") + ProtectedElements + " protegidos · " + ReparsePoints +
            WinSereno.Localization.LocalizationService.Source("Text.LinksSkipped") + InaccessibleElements + " inaccesibles/bloqueados";
    }
}
