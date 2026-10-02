using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WinSereno.Models;
namespace WinSereno.Services
{
    public static class AdapterRestartPolicy
    {
        public const int EnableAttempts = 3;
        public const int DisabledWaitSeconds = 5;
        public const int ActiveWaitSeconds = 15;
        public static RestartAdapter Select(IReadOnlyList<RestartAdapter> adapters, Func<IReadOnlyList<RestartAdapter>, RestartAdapter> choose)
        {
            if (adapters == null || adapters.Count == 0) return null;
            var selected = adapters.Count == 1 ? adapters[0] : choose(adapters);
            if (selected != null && !adapters.Contains(selected)) throw new InvalidOperationException("Adaptador ajeno a la selección interna.");
            return selected;
        }
        public static bool IsEligible(bool physical, bool active, string kind, string name, string description, string driver)
            => DhcpRenewalPolicy.IsEligible(physical, active, true, true, kind, name, description, driver);
        public static void ApplyAttempt(int attempt, AdapterRestartAttempt value)
        {
            var result = value.Result;
            bool confirmed = attempt == 0 ? value.State == AdapterRestartState.Disabled : value.State == AdapterRestartState.Enabled || value.State == AdapterRestartState.Active;
            result.ExecutionStatus = result.ExitCode == 0 ? ExecutionStatus.Success : ExecutionStatus.Failed;
            result.FindingStatus = confirmed && result.ExitCode == 0 ? FindingStatus.Completed : FindingStatus.Unknown;
            result.UserSummary = (attempt == 0 ? "Deshabilitar" : "Habilitar, intento " + attempt) +
                " · Estado verificado: " + value.State + " · ExitCode: " + (result.ExitCode?.ToString() ?? "no disponible");
        }
        public static async Task<MaintenanceTaskResult> RunAsync(string adapterName, Func<int, Task<AdapterRestartAttempt>> execute,
            Action<int, AdapterRestartAttempt> finished, Action<string> note, Func<Task> retryDelay)
        {
            var observer = finished; var report = note;
            // Observer/IPC failures must not interrupt the enable recovery policy.
            finished = (index, value) => { try { observer(index, value); } catch { } };
            note = text => { try { report(text); } catch { } };
            var total = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now };
            AdapterRestartAttempt disabled = null;
            try { disabled = await execute(0).ConfigureAwait(false); }
            catch (Exception ex) { note("Disable falló antes de obtener un resultado: " + ex.Message); }
            if (disabled != null) Record(total, 0, disabled, finished);
            bool confirmedDisable = disabled != null && disabled.Result.ExitCode == 0 && disabled.State == AdapterRestartState.Disabled;
            // Recovery is not a second restart. Restore an adapter that may have been disabled even if verification/IPC failed.
            bool recoveryNeeded = disabled != null && (disabled.State == AdapterRestartState.Disabled ||
                (disabled.CommandStarted && disabled.State == AdapterRestartState.Unknown));
            if (!confirmedDisable && !recoveryNeeded)
            {
                total.ExecutionStatus = ExecutionStatus.Failed; total.FindingStatus = FindingStatus.Unknown;
                total.UserSummary = "No se pudo deshabilitar «" + adapterName + "». Se detuvo el reinicio; habilitación omitida.";
                total.SequenceSteps.Add(new SequenceStepResult { TaskId = "enable", WasSkipped = true, SkipReason = "Disable falló; el adaptador no consta deshabilitado." });
                note(total.UserSummary); Finish(total); return total;
            }
            if (!confirmedDisable) note("Deshabilitado no confirmado correctamente; se detiene el reinicio y se intenta solo recuperar la habilitación.");
            bool restored = false, enableSucceeded = false; AdapterRestartState finalState = AdapterRestartState.Unknown;
            for (int attempt = 1; attempt <= EnableAttempts; attempt++)
            {
                if (attempt > 1) { note("Reintento de recuperación enable " + attempt + " de " + EnableAttempts); try { await retryDelay().ConfigureAwait(false); } catch (Exception ex) { note("Error en espera de reintento; se continúa la recuperación: " + ex.Message); } }
                AdapterRestartAttempt enabled = null;
                try { enabled = await execute(attempt).ConfigureAwait(false); }
                catch (Exception ex) { note("Enable intento " + attempt + " falló: " + ex.Message); }
                if (enabled == null) enabled = new AdapterRestartAttempt { Result = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now, FinishedAt = DateTimeOffset.Now, StdErr = "No se pudo ejecutar/verificar enable." } };
                Record(total, attempt, enabled, finished); finalState = enabled.State;
                if (finalState == AdapterRestartState.Enabled || finalState == AdapterRestartState.Active) { restored = true; enableSucceeded = enabled.Result.ExitCode == 0; break; }
            }
            if (!restored)
            {
                total.ExecutionStatus = ExecutionStatus.Failed; total.FindingStatus = FindingStatus.RepairFailed;
                total.UserSummary = "No se pudo confirmar la habilitación de «" + adapterName + "» tras " + EnableAttempts + " intentos. Puede haber quedado deshabilitado. Abre Configuración de Windows > Red e Internet > Configuración de red avanzada, o Panel de control > Conexiones de red, y habilita «" + adapterName + "» manualmente desde una sesión local.";
            }
            else if (!confirmedDisable || !enableSucceeded)
            { total.ExecutionStatus = ExecutionStatus.Failed; total.FindingStatus = FindingStatus.Attention; total.UserSummary = "El reinicio de «" + adapterName + "» no pudo verificarse, pero se confirmó su habilitación durante la recuperación."; }
            else
            {
                total.ExecutionStatus = ExecutionStatus.Success; total.FindingStatus = finalState == AdapterRestartState.Active ? FindingStatus.Completed : FindingStatus.Attention;
                total.UserSummary = finalState == AdapterRestartState.Active ? "«" + adapterName + "» se deshabilitó, volvió a habilitarse y está activo. Esto no confirma Internet." : "«" + adapterName + "» quedó habilitado, pero no volvió a estar conectado dentro del plazo de espera.";
            }
            Finish(total); return total;
        }
        private static void Record(MaintenanceTaskResult total, int index, AdapterRestartAttempt value, Action<int, AdapterRestartAttempt> finished)
        {
            ApplyAttempt(index, value);
            total.SequenceSteps.Add(new SequenceStepResult { TaskId = index == 0 ? "disable" : "enable." + index, Result = value.Result });
            finished(index, value);
        }
        private static void Finish(MaintenanceTaskResult result)
        { result.FinishedAt = DateTimeOffset.Now; result.Duration = result.FinishedAt - result.StartedAt; result.ExitCode = null; }
    }
}
