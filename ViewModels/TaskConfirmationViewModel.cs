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
                    case ImpactLevel.Maintenance: return "Mantenimiento";
                    case ImpactLevel.Configuration: return "Configuración";
                    case ImpactLevel.Repair: return "Reparación";
                    default: return "Información";
                }
            }
        }
        public string ElevationLabel => (Task.Id == WinSereno.Services.ElevatedTaskCatalog.FlushDnsId || Task.Id == WinSereno.Services.ElevatedTaskCatalog.RenewDhcpId) ? "Solo si Windows deniega permisos; se intenta primero sin elevación" : Task.RequiresElevation ? "Sí" : "No";
        public string RestartLabel => Task.MayRequireRestart ? "Puede ser necesario; no se reinicia automáticamente" : "No";
        public string CancellationLabel => Task.CanBeCancelled ? "Sí" : "No";
        public string ExactCommand => (Task.Id == WinSereno.Services.ElevatedTaskCatalog.CompleteId || Task.Id == WinSereno.Services.ElevatedTaskCatalog.RenewDhcpId || Task.Id == WinSereno.Services.ElevatedTaskCatalog.RestartAdapterId) ? Task.Command : Task.Command + " " + Task.Arguments;
        public string ExecutionNotice => Task.Id == WinSereno.Services.CleanupBatchExecutor.TaskId ? (Task.RequiresElevation ? "Se solicitarán permisos antes de comenzar cualquier borrado. Se omitirán los archivos en uso o protegidos." : "Se omitirán los archivos en uso o protegidos. No se solicitan permisos de administrador.") : Task.Id == WinSereno.Services.RecycleBinCleanupService.TaskId ? "Ejecutar vacía toda la Papelera del usuario actual. Sin UAC y sin segunda confirmación del Shell. Los archivos no podrán restaurarse desde la Papelera." : Task.Id == WinSereno.Services.ThumbnailsCleanupService.TaskId ? "Ejecutar elimina únicamente thumbcache_*.db elegibles. Sin UAC, sin cerrar ni reiniciar Explorer. Windows recreará las miniaturas cuando sean necesarias." : Task.Id == WinSereno.Services.UserTempCleanupService.TaskId ? "Ejecutar borra únicamente temporales del usuario elegibles. No solicita UAC ni cambia permisos. Los elementos en uso o inaccesibles se omiten." : Task.IsMock ? "Ejecutar inicia únicamente una simulación interna." : Task.Id == WinSereno.Services.ElevatedTaskCatalog.RenewDhcpId ? "Ejecutar libera y renueva DHCP solo en los adaptadores indicados. Puedes perder temporalmente la conexión. Solo se solicita UAC si Windows deniega permisos." : Task.Id == WinSereno.Services.ElevatedTaskCatalog.FlushDnsId ? "Ejecutar vacía la caché DNS. Solo se solicitará UAC si Windows exige permisos adicionales. La ventana principal seguirá sin privilegios." : "Ejecutar solicitará UAC para esta tarea. La ventana principal seguirá sin privilegios. Revisa la explicación y el comando antes de continuar.";
    }
}
