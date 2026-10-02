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
        public string ButtonLabel => Task.Id == ElevatedTaskCatalog.ComponentCleanupId ? "Limpiar componentes" : Task.Id == ElevatedTaskCatalog.CompleteId ? "Iniciar reparación completa" : (Task.Id == ElevatedTaskCatalog.ChkdskId || Task.Id == ElevatedTaskCatalog.CheckHealthId) ? "Comprobar" : Task.Id == ElevatedTaskCatalog.ScanHealthId ? "Analizar" : Task.Id == ElevatedTaskCatalog.RestoreHealthId ? "Reparar" : "Comprobar y reparar";
        public RepairTaskViewModel(string id, string technicalName, IntegritySessionState integrity)
        { try { Task = ElevatedTaskCatalog.Get(id); }
            catch (System.InvalidOperationException) when (id == ElevatedTaskCatalog.ChkdskId) { Task = new MaintenanceTask { Id = id, Name = "Comprobar disco (CHKDSK)", ShortDescription = "No se pudo determinar con seguridad la unidad de Windows. CHKDSK no se ejecutará.", CanBeCancelled = false }; }
            TechnicalName = technicalName; this.integrity = integrity; integrity.Changed += (s, e) => Raise(nameof(ResultText)); }
        public string ResultText { get { var r = integrity.Get(Task.Id); return r == null ? "Sin resultados durante esta sesión." : "Último resultado: " + (Task.Id == ElevatedTaskCatalog.ComponentCleanupId ? r.ExecutionStatus == ExecutionStatus.Cancelled ? "Cancelado" : r.FindingStatus == FindingStatus.Completed ? "Completado" : r.FindingStatus == FindingStatus.RestartRequired ? "Completado · Reinicio requerido" : r.ExecutionStatus == ExecutionStatus.Failed ? "Fallido" : "Desconocido" : new DiagnosticResult { Status = Task.Id == ElevatedTaskCatalog.ChkdskId && r.FindingStatus == FindingStatus.Attention ? DiagnosticStatus.Attention : IntegritySessionState.Status(r) }.StatusLabel) + " · " + r.UserSummary + " · Duración: " + r.Duration.TotalSeconds.ToString("0.0") + " s"; } }
    }
}
