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
        public string Summary => (Status == UserTempCleanupStatus.Completed ? "Completado" : Status == UserTempCleanupStatus.NothingToClean ? "Nada que limpiar" : Status == UserTempCleanupStatus.Partial ? "Completado parcialmente" : "Fallo de la operación") + " · " + DeletedFiles + " archivos eliminados · " +
            FormatBytes(RecoveredBytes) + " recuperados · " + DeletedDirectories +
            " carpetas vacías eliminadas · " + ProtectedElements + " protegidos · " + ReparsePoints +
            " enlaces omitidos · " + InaccessibleElements + " inaccesibles/bloqueados";
    }
}
