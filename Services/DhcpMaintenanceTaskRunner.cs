using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using WinSereno.Models;
namespace WinSereno.Services
{
    internal sealed class DhcpMaintenanceTaskRunner
    {
        private readonly ISessionLogger logger;
        private readonly OperationCoordinator operations;
        private readonly Dispatcher dispatcher;
        private readonly Action<TaskProgress> publish;
        public DhcpMaintenanceTaskRunner(ISessionLogger logger, OperationCoordinator operations, Dispatcher dispatcher, Action<TaskProgress> publish)
        { this.logger = logger; this.operations = operations; this.dispatcher = dispatcher; this.publish = publish; }
        public async Task<MaintenanceTaskResult> RunAsync(MaintenanceTask requested)
        {
            var approved = requested.DhcpPlan;
            if (approved == null || !approved.ReadSucceeded || approved.Adapters.Count == 0) throw new InvalidOperationException("No existe una selección DHCP interna confirmada.");
            var task = DhcpRenewalService.PrepareTask(approved);
            var service = new DhcpRenewalService(logger);
            using (var lease = operations.Begin(task.Name, false))
            {
                var result = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now };
                var watch = Stopwatch.StartNew(); var stdout = new StringBuilder(); var stderr = new StringBuilder();
                string label = "Revalidando interfaces DHCP confirmadas...", last = label;
                bool complete = false; Process worker = null;
                Action progress = () => publish(new TaskProgress { CurrentTask = task, State = RunnerState.Running, StartedAt = result.StartedAt,
                    Elapsed = watch.Elapsed, StepLabel = label, LastRelevantLine = last, StdOut = stdout.ToString(), StdErr = stderr.ToString() });
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                timer.Tick += (s, e) => progress(); timer.Start(); progress();
                Action<SequenceStepResult> skipped = skip => {
                    logger.Write("DHCP Skipped | TaskId=" + skip.TaskId + " | Motivo=" + skip.SkipReason);
                    stdout.AppendLine("[Omitido " + skip.TaskId + "] " + skip.SkipReason); progress();
                };
                Action<int, MaintenanceTaskResult> finished = (ordinal, commandResult) => {
                    var command = DhcpRenewalService.Command(approved, ordinal);
                    logger.TaskFinished(command, commandResult);
                    stdout.AppendLine("[Resultado WinSereno] " + command.Name + " | ExitCode=" + (commandResult.ExitCode?.ToString() ?? "no disponible") +
                        " | " + commandResult.ExecutionStatus + " / " + commandResult.FindingStatus + " | Duración=" + commandResult.Duration + " | " + commandResult.UserSummary);
                    progress();
                };
                try
                {
                    logger.Write("Confirmación DHCP aceptada | TaskId=" + task.Id + " | Comandos exactos:\n" + task.Command);
                    var current = await Task.Run(() => service.ReadPlan());
                    if (!current.ReadSucceeded || DhcpRenewalService.Fingerprint(current) != DhcpRenewalService.Fingerprint(approved))
                        throw new InvalidOperationException("La selección DHCP cambió después de confirmar. No se ejecuta ningún comando.");
                    logger.TaskStarted(task); logger.Write("DHCP: intento sin elevación.");
                    var direct = await DhcpRenewalPolicy.RunAsync(approved.Adapters.Count, 0, async ordinal =>
                    {
                        var command = DhcpRenewalService.Command(approved, ordinal);
                        label = "Paso " + (ordinal + 1) + " de " + (approved.Adapters.Count * 2) + " · " + command.Name;
                        last = label; stdout.AppendLine("\n=== " + command.Command + " " + command.Arguments + " ==="); progress();
                        var commandResult = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now };
                        DhcpVerification verification = DhcpVerification.Unknown;
                        try
                        {
                            commandResult = await Task.Run(() => FixedTaskProcess.RunDhcpCommandAsync(service, approved, ordinal,
                                began => dispatcher.Invoke(() => logger.TaskStarted(command)),
                                text => dispatcher.Invoke(() => { stdout.Append(text); last = Relevant(text, last); logger.Write(command.Id + " stdout: " + text); progress(); }),
                                text => dispatcher.Invoke(() => { stderr.Append(text); last = Relevant(text, last); logger.Write(command.Id + " stderr: " + text); progress(); })));
                            if (commandResult.ExitCode == 0) verification = await Task.Run(() => service.Verify(approved, ordinal));
                        }
                        catch (Exception ex)
                        {
                            commandResult.FinishedAt = DateTimeOffset.Now; commandResult.Duration = commandResult.FinishedAt - commandResult.StartedAt;
                            commandResult.StdErr = ex.GetType().Name + ": " + ex.Message; stderr.AppendLine(commandResult.StdErr); logger.Write("Error DHCP: " + ex);
                        }
                        DhcpRenewalPolicy.ApplyCommand(ordinal % 2 == 0, commandResult, verification); finished(ordinal, commandResult); return commandResult;
                    }, ordinal => DhcpRenewalService.StepId(approved, ordinal), skip => dispatcher.Invoke(() => skipped(skip)), true);
                    foreach (var step in direct.Steps) result.SequenceSteps.Add(step);
                    if (!direct.ElevationResumeOrdinal.HasValue) { complete = true; }
                    else
                    {
                        int next = direct.ElevationResumeOrdinal.Value;
                        label = "Windows denegó permisos; solicitando elevación para continuar..."; last = label; progress();
                        logger.Write("DHCP: denegación de permisos | Reanudar paso=" + next + " | Solicitud de elevación del mismo EXE.");
                        string pipeId = Guid.NewGuid().ToString("N"); byte[] bytes = new byte[32]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
                        string nonce = BitConverter.ToString(bytes).Replace("-", ""); var sid = WindowsIdentity.GetCurrent().User;
                        var acl = new PipeSecurity(); acl.SetAccessRuleProtection(true, false); acl.SetOwner(sid);
                        acl.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
                        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
                        using (var pipe = new NamedPipeServerStream("WinSereno-" + pipeId, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, acl))
                        {
                            var connecting = pipe.WaitForConnectionAsync();
                            worker = await Task.Run(() => Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
                                "--elevated-worker " + task.Id + " " + pipeId + " " + nonce + " " + Process.GetCurrentProcess().Id) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory }));
                            if (worker == null) throw new InvalidOperationException("El worker no se inició.");
                            await WorkerProtocol.WithTimeout(WaitConnected(connecting), pipe);
                            if (!WorkerProtocol.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid) || pid != worker.Id) throw new InvalidDataException("Cliente IPC no autorizado.");
                            using (var reader = new BinaryReader(pipe, Encoding.UTF8, true))
                            using (var writer = new BinaryWriter(pipe, Encoding.UTF8, true))
                            {
                                bool valid = await WorkerProtocol.WithTimeout(Task.Run(() => WorkerProtocol.ReadText(reader) == "WM1" && WorkerProtocol.ReadText(reader) == nonce), pipe);
                                writer.Write(valid); writer.Flush(); if (!valid) throw new InvalidDataException("Handshake IPC inválido.");
                                // This is consent metadata, never executable names, adapter arguments or a command list.
                                WorkerProtocol.WriteText(writer, DhcpRenewalService.Fingerprint(approved)); writer.Write(next); writer.Flush();
                                MaintenanceTaskResult active = null; var stepOut = new StringBuilder(); var stepErr = new StringBuilder();
                                while (!complete)
                                {
                                    var message = await Task.Run(() => (WorkerMessage)reader.ReadByte());
                                    if (message == WorkerMessage.WorkerStarted) { logger.Write("Worker DHCP elevado autenticado."); continue; }
                                    if (message == WorkerMessage.TaskFailed) throw new IOException("Worker DHCP: " + await Task.Run(() => WorkerProtocol.ReadText(reader)));
                                    if (message == WorkerMessage.DhcpCompleted)
                                    { if (active != null || next != approved.Adapters.Count * 2) throw new InvalidDataException("Finalización DHCP incompleta."); complete = true; break; }
                                    if (message == WorkerMessage.DhcpStepStarted)
                                    {
                                        int ordinal = await Task.Run(() => reader.ReadInt32());
                                        if (ordinal != next || active != null || ordinal >= approved.Adapters.Count * 2) throw new InvalidDataException("Paso DHCP fuera de selección.");
                                        active = new MaintenanceTaskResult { StartedAt = new DateTimeOffset(await Task.Run(() => reader.ReadInt64()), TimeSpan.Zero).ToLocalTime() };
                                        stepOut.Clear(); stepErr.Clear(); var command = DhcpRenewalService.Command(approved, next); command.RequiresElevation = true;
                                        label = "Paso " + (next + 1) + " de " + (approved.Adapters.Count * 2) + " · " + command.Name;
                                        stdout.AppendLine("\n=== " + command.Command + " " + command.Arguments + " ==="); last = label; logger.TaskStarted(command);
                                    }
                                    else if (message == WorkerMessage.StdOutLine || message == WorkerMessage.StdErrLine)
                                    {
                                        string text = await Task.Run(() => WorkerProtocol.ReadText(reader));
                                        if (message == WorkerMessage.StdOutLine) { stdout.Append(text); stepOut.Append(text); } else { stderr.Append(text); stepErr.Append(text); }
                                        last = Relevant(text, last); logger.Write("DHCP paso " + next + " " + (message == WorkerMessage.StdOutLine ? "stdout: " : "stderr: ") + text);
                                    }
                                    else if (message == WorkerMessage.DhcpStepCompleted)
                                    {
                                        int ordinal = await Task.Run(() => reader.ReadInt32()); if (ordinal != next || ordinal >= approved.Adapters.Count * 2) throw new InvalidDataException("Resultado DHCP fuera de selección.");
                                        bool hasCode = await Task.Run(() => reader.ReadBoolean()); int? code = hasCode ? (int?)await Task.Run(() => reader.ReadInt32()) : null;
                                        var began = new DateTimeOffset(await Task.Run(() => reader.ReadInt64()), TimeSpan.Zero).ToLocalTime();
                                        var ended = new DateTimeOffset(await Task.Run(() => reader.ReadInt64()), TimeSpan.Zero).ToLocalTime();
                                        var verification = (DhcpVerification)await Task.Run(() => reader.ReadByte());
                                        if (!Enum.IsDefined(typeof(DhcpVerification), verification)) throw new InvalidDataException("Verificación DHCP desconocida.");
                                        active = active ?? new MaintenanceTaskResult(); active.StartedAt = began; active.FinishedAt = ended; active.Duration = ended - began;
                                        active.ExitCode = code; active.StdOut = stepOut.ToString(); active.StdErr = stepErr.ToString();
                                        DhcpRenewalPolicy.ApplyCommand(next % 2 == 0, active, verification); finished(next, active);
                                        result.SequenceSteps.Add(new SequenceStepResult { TaskId = DhcpRenewalService.StepId(approved, next), Result = active }); active = null; next++;
                                        stepOut.Clear(); stepErr.Clear();
                                    }
                                    else if (message == WorkerMessage.DhcpStepSkipped)
                                    {
                                        int ordinal = await Task.Run(() => reader.ReadInt32()); if (ordinal != next || active != null || ordinal >= approved.Adapters.Count * 2) throw new InvalidDataException("Omisión DHCP fuera de selección.");
                                        string reason = await Task.Run(() => WorkerProtocol.ReadText(reader));
                                        var skip = new SequenceStepResult { TaskId = DhcpRenewalService.StepId(approved, next), WasSkipped = true, SkipReason = reason };
                                        result.SequenceSteps.Add(skip); skipped(skip); next++;
                                    }
                                    else throw new InvalidDataException("Mensaje DHCP inesperado.");
                                    progress();
                                }
                            }
                        }
                    }
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                { result.ExecutionStatus = ExecutionStatus.Cancelled; logger.Write("Elevación DHCP cancelada por el usuario."); }
                catch (Exception ex) { result.ExecutionStatus = ExecutionStatus.Failed; logger.Write("Error DHCP: " + ex); }
                finally
                {
                    if (worker != null)
                    {
                        try { if (!worker.HasExited) { label = "Esperando al worker DHCP no cancelable..."; await Task.Run(() => worker.WaitForExit()); } }
                        catch (Exception ex) { logger.Write("Error esperando worker DHCP: " + ex); }
                        worker.Dispose();
                    }
                    foreach (int ordinal in Enumerable.Range(0, approved.Adapters.Count * 2))
                    {
                        string id = DhcpRenewalService.StepId(approved, ordinal);
                        if (result.SequenceSteps.All(s => s.TaskId != id))
                        { var skip = new SequenceStepResult { TaskId = id, WasSkipped = true, SkipReason = "La operación no llegó a este comando; selección cambiada, cancelación o fallo técnico." }; result.SequenceSteps.Add(skip); skipped(skip); }
                    }
                    bool cancelled = result.ExecutionStatus == ExecutionStatus.Cancelled;
                    DhcpRenewalPolicy.Summarize(result, Enumerable.Range(0, approved.Adapters.Count * 2).Select(o => DhcpRenewalService.StepId(approved, o)));
                    if (!complete && !cancelled) { result.ExecutionStatus = ExecutionStatus.Failed; result.FindingStatus = FindingStatus.Unknown; result.UserSummary = "La renovación DHCP se interrumpió por un cambio de selección o fallo técnico. Consulta los pasos ejecutados y omitidos."; }
                    timer.Stop(); watch.Stop(); result.FinishedAt = DateTimeOffset.Now; result.Duration = watch.Elapsed;
                    result.StdOut = stdout.ToString(); result.StdErr = stderr.ToString(); logger.TaskFinished(task, result);
                    publish(new TaskProgress { CurrentTask = task, State = RunnerState.Completed, StartedAt = result.StartedAt, Elapsed = result.Duration,
                        LastRelevantLine = result.UserSummary, StdOut = result.StdOut, StdErr = result.StdErr, Result = result });
                }
                return result;
            }
        }
        private static string Relevant(string text, string previous) => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? previous;
        private static async Task<bool> WaitConnected(Task task) { await task.ConfigureAwait(false); return true; }
    }
}
