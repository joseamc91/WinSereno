using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinSereno.Models;

namespace WinSereno.Services
{
    public sealed class MockMaintenanceTaskRunner : IMaintenanceTaskRunner
    {
        private readonly object sync = new object();
        private readonly ISessionLogger logger;
        private readonly OperationCoordinator operations;
        private CancellationTokenSource cancellation;
        private TaskCompletionSource<bool> idle;
        public bool IsActive { get; private set; }
        public TaskProgress Current { get; private set; } = new TaskProgress { State = RunnerState.Idle };
        public event EventHandler<TaskProgress> ProgressChanged;
        public MockMaintenanceTaskRunner(ISessionLogger logger, OperationCoordinator operations) { this.logger = logger; this.operations = operations; }

        public async Task<MaintenanceTaskResult> RunAsync(MaintenanceTask task)
        {
            if (task == null || !task.IsMock || task.TaskType != TaskType.Internal || task.Command != "(simulación interna)")
                throw new NotSupportedException("Este motor solo admite simulaciones internas. No ejecuta comandos.");
            var operation = operations.Begin(task.Name, task.CanBeCancelled);
            lock (sync)
            {
                if (IsActive) throw new InvalidOperationException("Ya hay una tarea activa en la aplicación.");
                IsActive = true;
                cancellation = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
                idle = new TaskCompletionSource<bool>();
                Current = new TaskProgress { CurrentTask = task, State = RunnerState.Running, StartedAt = DateTimeOffset.Now };
            }
            var result = new MaintenanceTaskResult { StartedAt = Current.StartedAt.Value };
            var watch = Stopwatch.StartNew();
            var stdout = new StringBuilder("[MOCK] Simulación interna. Ningún comando del sistema será ejecutado.\n");
            var stderr = new StringBuilder();
            try
            {
                logger.TaskStarted(task);
                Publish(watch.Elapsed, stdout.ToString(), "", "Ejecutando simulación...");
                // Time and output are mock data; no percentage or maintenance stages are invented.
                while (watch.Elapsed < TimeSpan.FromSeconds(12))
                {
                    await Task.Delay(250, cancellation.Token);
                    Publish(watch.Elapsed, stdout.ToString(), stderr.ToString(), "Ejecutando simulación...");
                }
                stdout.AppendLine("[MOCK] Ejecución terminada. Ejemplo: se ha detectado una necesidad de reparación ficticia.");
                stderr.AppendLine("[MOCK] Salida de error de ejemplo; no corresponde a un fallo real de Windows.");
                result.ExecutionStatus = ExecutionStatus.Success;
                result.FindingStatus = FindingStatus.RepairRequired;
                result.ExitCode = 0;
                result.UserSummary = "Simulación finalizada: ejecución correcta y reparación requerida ficticia. Windows no ha cambiado.";
            }
            catch (OperationCanceledException)
            {
                stdout.AppendLine("[MOCK] Cancelada por el usuario.");
                result.ExecutionStatus = ExecutionStatus.Cancelled;
                result.UserSummary = "Simulación cancelada. Windows no ha cambiado.";
            }
            catch (Exception ex)
            {
                stderr.AppendLine(ex.Message);
                result.ExecutionStatus = ExecutionStatus.Failed;
                result.UserSummary = "La simulación ha fallado: " + ex.Message;
            }
            finally
            {
                watch.Stop();
                result.FinishedAt = DateTimeOffset.Now;
                result.Duration = watch.Elapsed;
                result.StdOut = stdout.ToString();
                result.StdErr = stderr.ToString();
                try { logger.TaskFinished(task, result); }
                finally
                {
                    lock (sync)
                    {
                        IsActive = false;
                        cancellation.Dispose();
                        cancellation = null;
                        Current = new TaskProgress { CurrentTask = task, State = RunnerState.Completed, StartedAt = result.StartedAt,
                            Elapsed = result.Duration, LastRelevantLine = result.UserSummary, StdOut = result.StdOut, StdErr = result.StdErr, Result = result };
                        idle.TrySetResult(true);
                    }
                    try { ProgressChanged?.Invoke(this, Current); }
                    finally { operation.Dispose(); }
                }
            }
            return result;
        }
        private void Publish(TimeSpan elapsed, string stdout, string stderr, string line)
        {
            Current = new TaskProgress { CurrentTask = Current.CurrentTask, StartedAt = Current.StartedAt,
                State = cancellation.IsCancellationRequested ? RunnerState.Cancelling : RunnerState.Running,
                Elapsed = elapsed, LastRelevantLine = line, StdOut = stdout, StdErr = stderr, Percentage = null };
            ProgressChanged?.Invoke(this, Current);
        }
        public bool RequestCancellation()
        {
            lock (sync)
            {
                if (!IsActive || !Current.CurrentTask.CanBeCancelled) return false;
                cancellation.Cancel();
                return true;
            }
        }
        public Task WaitForIdleAsync()
        {
            lock (sync) return IsActive ? idle.Task : Task.CompletedTask;
        }
    }
}
