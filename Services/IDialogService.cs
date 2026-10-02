using WinSereno.Models;

namespace WinSereno.Services
{
    public interface IDialogService
    {
        RestartAdapter SelectRestartAdapter(System.Collections.Generic.IReadOnlyList<RestartAdapter> adapters);
        bool ConfirmTask(MaintenanceTask task);
        void ShowOutput(TaskProgress progress);
        void ShowDiagnosticDetails(DiagnosticResult result);
        void ShowMessage(string message);
        bool ConfirmCancelAndClose();
    }
}
