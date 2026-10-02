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
        public string DisplayTitle => Task.Id == ElevatedTaskCatalog.ChkdskId ? "Comprobar disco" : Task.Name;
        public string DisplayTechnicalName
        {
            get
            {
                switch (Task.Id)
                {
                    case ElevatedTaskCatalog.CheckHealthId: return "DISM /CheckHealth";
                    case ElevatedTaskCatalog.ScanHealthId: return "DISM /ScanHealth";
                    case ElevatedTaskCatalog.RestoreHealthId: return "DISM /RestoreHealth";
                    case ElevatedTaskCatalog.SfcId: return "SFC /scannow";
                    case ElevatedTaskCatalog.ComponentCleanupId: return "DISM /StartComponentCleanup";
                    case ElevatedTaskCatalog.ChkdskId: return "CHKDSK";
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
                    case ElevatedTaskCatalog.CheckHealthId: return "Comprueba rápidamente si Windows tiene registrada corrupción en el almacén de componentes.";
                    case ElevatedTaskCatalog.ScanHealthId: return "Busca corrupción en profundidad sin realizar reparaciones.";
                    case ElevatedTaskCatalog.RestoreHealthId: return "Busca y repara corrupción del almacén de componentes de Windows.";
                    case ElevatedTaskCatalog.SfcId: return "Comprueba los archivos protegidos de Windows y repara los dañados cuando es posible.";
                    case ElevatedTaskCatalog.ComponentCleanupId: return "Elimina versiones reemplazadas de componentes que Windows ya no necesita.";
                    case ElevatedTaskCatalog.ChkdskId: return "Comprueba el sistema de archivos de la unidad de Windows sin realizar reparaciones.";
                    default: return Task.ShortDescription;
                }
            }
        }
        public string ButtonLabel => Task.Id == ElevatedTaskCatalog.ComponentCleanupId ? "Limpiar componentes" : Task.Id == ElevatedTaskCatalog.CompleteId ? "Iniciar reparación completa" : (Task.Id == ElevatedTaskCatalog.ChkdskId || Task.Id == ElevatedTaskCatalog.CheckHealthId) ? "Comprobar" : Task.Id == ElevatedTaskCatalog.ScanHealthId ? "Analizar" : Task.Id == ElevatedTaskCatalog.RestoreHealthId ? "Reparar" : "Comprobar y reparar";
        public RepairTaskViewModel(string id, string technicalName, IntegritySessionState integrity)
        { try { Task = ElevatedTaskCatalog.Get(id); }
            catch (System.InvalidOperationException) when (id == ElevatedTaskCatalog.ChkdskId) { Task = new MaintenanceTask { Id = id, Name = "Comprobar disco (CHKDSK)", ShortDescription = "No se pudo determinar con seguridad la unidad de Windows. CHKDSK no se ejecutará.", CanBeCancelled = false }; }
            TechnicalName = technicalName; this.integrity = integrity; integrity.Changed += (s, e) => Raise(nameof(ResultText)); }
        public string ResultText { get { var r = integrity.Get(Task.Id); return r == null ? "Sin resultados durante esta sesión." : "Último resultado: " + (Task.Id == ElevatedTaskCatalog.ComponentCleanupId ? r.ExecutionStatus == ExecutionStatus.Cancelled ? "Cancelado" : r.FindingStatus == FindingStatus.Completed ? "Completado" : r.FindingStatus == FindingStatus.RestartRequired ? "Completado · Reinicio requerido" : r.ExecutionStatus == ExecutionStatus.Failed ? "Fallido" : "Desconocido" : new DiagnosticResult { Status = Task.Id == ElevatedTaskCatalog.ChkdskId && r.FindingStatus == FindingStatus.Attention ? DiagnosticStatus.Attention : IntegritySessionState.Status(r) }.StatusLabel) + " · " + r.UserSummary + " · Duración: " + r.Duration.TotalSeconds.ToString("0.0") + " s"; } }
    }
}
