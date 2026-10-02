using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using WinSereno.Models;

namespace WinSereno.Services
{
    internal static class ElevatedWorker
    {
        public static async Task<int> RunAsync(string[] args)
        {
            // Strict positional metadata, never an executable/argument supplied by the caller.
            if (args.Length != 5 || args[0] != "--elevated-worker" || !ElevatedTaskCatalog.IsAllowed(args[1]) ||
                !Guid.TryParseExact(args[2], "N", out Guid pipeId) || args[3].Length != 64 ||
                !System.Text.RegularExpressions.Regex.IsMatch(args[3], "\\A[0-9A-F]{64}\\z") || !int.TryParse(args[4], out int parentPid) || parentPid <= 0)
                return 2;
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return 3;
            using (var pipe = new NamedPipeClientStream(".", "WinSereno-" + pipeId.ToString("N"), PipeDirection.InOut,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
            {
                try
                {
                    await pipe.ConnectAsync(WorkerProtocol.HandshakeTimeout).ConfigureAwait(false);
                    if (!WorkerProtocol.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint serverPid) || serverPid != parentPid) return 4;
                    using (var parent = Process.GetProcessById(parentPid))
                        if (!string.Equals(parent.MainModule.FileName, Process.GetCurrentProcess().MainModule.FileName, StringComparison.OrdinalIgnoreCase)) return 4;
                    using (var reader = new BinaryReader(pipe, Encoding.UTF8, true))
                    using (var writer = new BinaryWriter(pipe, Encoding.UTF8, true))
                    {
                        WorkerProtocol.WriteText(writer, "WM1"); WorkerProtocol.WriteText(writer, args[3]); writer.Flush();
                        bool accepted = await WorkerProtocol.WithTimeout(Task.Run(() => reader.ReadBoolean()), pipe).ConfigureAwait(false);
                        if (!accepted) return 4;
                        writer.Write((byte)WorkerMessage.WorkerStarted); writer.Flush();
                        if (args[1] == ElevatedTaskCatalog.CleanupSelectedId)
                        {
                            bool executed = false;
                            while (true)
                            {
                                byte request = await Task.Run(() => reader.ReadByte()).ConfigureAwait(false);
                                if (request == 2) return 0;
                                if (request == 3 && executed) {
                                    var analysis = await Task.Run(() => new CleanupAnalysisService(null).AnalyzeWindowsTemporary()).ConfigureAwait(false);
                                    writer.Write((byte)WorkerMessage.WindowsTempAnalyzed); WindowsTempAnalysisProtocol.Write(writer, analysis); writer.Flush(); continue;
                                }
                                if (request != 1 || executed) return 6;
                                executed = true; bool connected = true;
                                var result = await WindowsTempCleanupService.RunWithCountersAsync(value => {
                                    if (!connected) return;
                                    try { var counts = CleanupCounters.FromFiles(value); writer.Write((byte)WorkerMessage.CleanupBatchCounters);
                                        writer.Write(counts.ReleasedBytes); writer.Write(counts.RemovedElements); writer.Write(counts.SkippedElements); writer.Flush(); }
                                    catch (IOException) { connected = false; } catch (ObjectDisposedException) { connected = false; }
                                }).ConfigureAwait(false);
                                if (!connected) return 5;
                                writer.Write((byte)WorkerMessage.WindowsTempCleaned); WindowsTempCleanupProtocol.Write(writer, result); writer.Flush();
                            }
                        }
                        if (args[1] == ElevatedTaskCatalog.WindowsTempCleanupId)
                        {
                            writer.Write((byte)WorkerMessage.TaskStarted); writer.Write(DateTimeOffset.UtcNow.UtcTicks); writer.Flush();
                            bool connected = true;
                            Action<string> progress = text => {
                                if (!connected) return;
                                try { writer.Write((byte)WorkerMessage.WindowsTempCleanupProgress); WorkerProtocol.WriteText(writer, text); writer.Flush(); }
                                catch (IOException) { connected = false; } catch (ObjectDisposedException) { connected = false; }
                            };
                            var result = await WindowsTempCleanupService.RunAsync(progress).ConfigureAwait(false);
                            if (connected) { writer.Write((byte)WorkerMessage.WindowsTempCleaned); WindowsTempCleanupProtocol.Write(writer, result); writer.Flush(); }
                            return 0;
                        }
                        if (args[1] == ElevatedTaskCatalog.WindowsTempAnalyzeId)
                        {
                            writer.Write((byte)WorkerMessage.TaskStarted); writer.Write(DateTimeOffset.UtcNow.UtcTicks); writer.Flush();
                            var analysis = await Task.Run(() => new CleanupAnalysisService(null).AnalyzeWindowsTemporary()).ConfigureAwait(false);
                            writer.Write((byte)WorkerMessage.WindowsTempAnalyzed);
                            WindowsTempAnalysisProtocol.Write(writer, analysis); writer.Flush(); return 0;
                        }
                        if (args[1] == ElevatedTaskCatalog.RestartAdapterId)
                        {
                            try
                            {
                                string fingerprint = await WorkerProtocol.WithTimeout(Task.Run(() => WorkerProtocol.ReadText(reader)), pipe).ConfigureAwait(false);
                                var service = new AdapterRestartService(null);
                                var adapter = service.ResolveConfirmed(fingerprint);
                                await ExecuteRestartAsync(writer, service, adapter).ConfigureAwait(false); return 0;
                            }
                            catch (Exception ex) { writer.Write((byte)WorkerMessage.TaskFailed); WorkerProtocol.WriteText(writer, ex.GetType().Name + ": " + ex.Message); writer.Flush(); return 5; }
                        }
                        if (args[1] == ElevatedTaskCatalog.RenewDhcpId)
                        {
                            try
                            {
                                string fingerprint = await WorkerProtocol.WithTimeout(Task.Run(() => WorkerProtocol.ReadText(reader)), pipe).ConfigureAwait(false);
                                int resume = await WorkerProtocol.WithTimeout(Task.Run(() => reader.ReadInt32()), pipe).ConfigureAwait(false);
                                var service = new DhcpRenewalService(null); var plan = service.ReadPlan();
                                if (!plan.ReadSucceeded || plan.Adapters.Count == 0 || fingerprint.Length != 64 || fingerprint != DhcpRenewalService.Fingerprint(plan) || resume < 0 || resume >= plan.Adapters.Count * 2)
                                    throw new InvalidOperationException("La selección DHCP confirmada ha cambiado; no se ejecutan comandos.");
                                if (resume % 2 == 1 && service.Verify(plan, resume - 1) != DhcpVerification.Released)
                                    throw new InvalidOperationException("No se pudo confirmar release para reanudar renew; no se ejecuta.");
                                await ExecuteDhcpAsync(writer, service, plan, resume).ConfigureAwait(false); return 0;
                            }
                            catch (Exception ex) { writer.Write((byte)WorkerMessage.TaskFailed); WorkerProtocol.WriteText(writer, ex.GetType().Name + ": " + ex.Message); writer.Flush(); return 5; }
                        }
                        if (args[1] == ElevatedTaskCatalog.ResetTcpIpId)
                        {
                            try
                            {
                                string fingerprint = await WorkerProtocol.WithTimeout(Task.Run(() => WorkerProtocol.ReadText(reader)), pipe).ConfigureAwait(false);
                                bool warningAccepted = await WorkerProtocol.WithTimeout(Task.Run(() => reader.ReadBoolean()), pipe).ConfigureAwait(false);
                                var current = await Task.Run(() => new TcpIpResetPreflightService(null).Read()).ConfigureAwait(false);
                                TcpIpResetPreflightService.ValidateContext(current, fingerprint, warningAccepted);
                                writer.Write((byte)WorkerMessage.StdOutLine);
                                WorkerProtocol.WriteText(writer, "[WinSereno] Contexto IPv4 revalidado antes de TCP/IP; interfaces=" + current.Interfaces.Count + ".\r\n"); writer.Flush();
                            }
                            catch (Exception ex) { writer.Write((byte)WorkerMessage.TaskFailed); WorkerProtocol.WriteText(writer, ex.GetType().Name + ": " + ex.Message); writer.Flush(); return 5; }
                        }
                        if (args[1] == ElevatedTaskCatalog.DiagnosticIntegrityId && !await Task.Run(() => reader.ReadBoolean()).ConfigureAwait(false)) return 0;
                        return await ExecuteAsync(writer, args[1]).ConfigureAwait(false);
                    }
                }
                catch { return 5; } // Parent records transport failures. No elevated config/session writes.
            }
        }
        private static async Task ExecuteRestartAsync(BinaryWriter writer, AdapterRestartService service, RestartAdapter adapter)
        {
            var outputLock = new object(); bool connected = true;
            // Loss of IPC must never prevent local enable/recovery. Drain output and finish the fixed recovery policy.
            Action<Action> send = action => { lock (outputLock) { if (!connected) return; try { action(); writer.Flush(); } catch (IOException) { connected = false; } catch (ObjectDisposedException) { connected = false; } } };
            Action<string> note = text => send(() => { writer.Write((byte)WorkerMessage.RestartNote); WorkerProtocol.WriteText(writer, text); });
            service.DiagnosticMessage = note;
            var total = await AdapterRestartPolicy.RunAsync(adapter.Name, async attempt =>
            {
                var value = new AdapterRestartAttempt { Result = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now } };
                send(() => { writer.Write((byte)WorkerMessage.RestartStepStarted); writer.Write(attempt); });
                try
                {
                    value.Result = await FixedTaskProcess.RunRestartCommandAsync(service, adapter, attempt,
                        began => { value.CommandStarted = true; note("Proceso iniciado: " + (attempt == 0 ? "disable" : "enable intento " + attempt)); },
                        text => send(() => { writer.Write((byte)WorkerMessage.StdOutLine); WorkerProtocol.WriteText(writer, text); }),
                        text => send(() => { writer.Write((byte)WorkerMessage.StdErrLine); WorkerProtocol.WriteText(writer, text); })).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    value.Result.FinishedAt = DateTimeOffset.Now; value.Result.Duration = value.Result.FinishedAt - value.Result.StartedAt;
                    value.Result.StdErr = ex.GetType().Name + ": " + ex.Message;
                    send(() => { writer.Write((byte)WorkerMessage.StdErrLine); WorkerProtocol.WriteText(writer, value.Result.StdErr); });
                }
                if (value.CommandStarted)
                {
                    note(attempt == 0 ? "Verificando deshabilitado en Windows..." : "Esperando habilitación y reconexión...");
                    value.State = value.Result.ExitCode == 0 ? await service.WaitStateAsync(adapter, attempt == 0).ConfigureAwait(false) : service.ReadState(adapter);
                }
                note("Estado del sistema tras " + (attempt == 0 ? "disable" : "enable " + attempt) + ": " + value.State);
                return value;
            }, (attempt, value) => send(() => {
                writer.Write((byte)WorkerMessage.RestartStepCompleted); writer.Write(attempt);
                writer.Write(value.Result.ExitCode.HasValue); if (value.Result.ExitCode.HasValue) writer.Write(value.Result.ExitCode.Value);
                writer.Write(value.Result.StartedAt.UtcTicks); writer.Write(value.Result.FinishedAt.UtcTicks);
                writer.Write(value.CommandStarted); writer.Write((byte)value.State);
            }), note, () => Task.Delay(1000)).ConfigureAwait(false);
            foreach (var skip in total.SequenceSteps.Where(s => s.WasSkipped)) note("Skipped | " + skip.TaskId + " | " + skip.SkipReason);
            send(() => { writer.Write((byte)WorkerMessage.RestartCompleted); writer.Write((byte)total.ExecutionStatus);
                writer.Write((byte)total.FindingStatus); WorkerProtocol.WriteText(writer, total.UserSummary); });
        }
        private static async Task ExecuteDhcpAsync(BinaryWriter writer, DhcpRenewalService service, DhcpRenewalPlan plan, int resume)
        {
            var outputLock = new object(); bool connected = true;
            Action<Action> send = action => { lock (outputLock) { if (!connected) return; try { action(); writer.Flush(); } catch (IOException) { connected = false; } catch (ObjectDisposedException) { connected = false; } } };
            await DhcpRenewalPolicy.RunAsync(plan.Adapters.Count, resume, async ordinal =>
            {
                var result = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now };
                DhcpVerification verification = DhcpVerification.Unknown;
                try
                {
                    result = await FixedTaskProcess.RunDhcpCommandAsync(service, plan, ordinal,
                        began => send(() => { writer.Write((byte)WorkerMessage.DhcpStepStarted); writer.Write(ordinal); writer.Write(began.UtcTicks); }),
                        text => send(() => { writer.Write((byte)WorkerMessage.StdOutLine); WorkerProtocol.WriteText(writer, text); }),
                        text => send(() => { writer.Write((byte)WorkerMessage.StdErrLine); WorkerProtocol.WriteText(writer, text); })).ConfigureAwait(false);
                    if (result.ExitCode == 0) verification = service.Verify(plan, ordinal);
                }
                catch (Exception ex)
                {
                    result.FinishedAt = DateTimeOffset.Now; result.Duration = result.FinishedAt - result.StartedAt;
                    result.ExecutionStatus = ExecutionStatus.Failed; result.StdErr = ex.GetType().Name + ": " + ex.Message;
                    send(() => { writer.Write((byte)WorkerMessage.StdErrLine); WorkerProtocol.WriteText(writer, result.StdErr); });
                }
                DhcpRenewalPolicy.ApplyCommand(ordinal % 2 == 0, result, verification);
                send(() => {
                    writer.Write((byte)WorkerMessage.DhcpStepCompleted); writer.Write(ordinal); writer.Write(result.ExitCode.HasValue);
                    if (result.ExitCode.HasValue) writer.Write(result.ExitCode.Value);
                    writer.Write(result.StartedAt.UtcTicks); writer.Write(result.FinishedAt.UtcTicks); writer.Write((byte)verification);
                });
                return result;
            }, ordinal => DhcpRenewalService.StepId(plan, ordinal), skip => send(() => {
                int ordinal = Enumerable.Range(0, plan.Adapters.Count * 2).Single(o => DhcpRenewalService.StepId(plan, o) == skip.TaskId);
                writer.Write((byte)WorkerMessage.DhcpStepSkipped); writer.Write(ordinal); WorkerProtocol.WriteText(writer, skip.SkipReason);
            }), false, () => connected).ConfigureAwait(false);
            send(() => writer.Write((byte)WorkerMessage.DhcpCompleted));
        }
        private static async Task<int> ExecuteAsync(BinaryWriter writer, string taskId)
        {
            if (taskId == ElevatedTaskCatalog.DiagnosticIntegrityId)
            {
                await DiagnosticIntegritySequence.RunAsync(async id => {
                    writer.Write((byte)WorkerMessage.StepStarted); WorkerProtocol.WriteText(writer, id); writer.Flush();
                    return await ExecuteSingleAsync(writer, id, true, true).ConfigureAwait(false);
                }).ConfigureAwait(false);
                writer.Write((byte)WorkerMessage.SequenceCompleted); writer.Flush(); return 0;
            }
            if (taskId != ElevatedTaskCatalog.CompleteId)
            { await ExecuteSingleAsync(writer, taskId, false).ConfigureAwait(false); return 0; }
            await RepairCompleteSequence.RunAsync(async id =>
            {
                writer.Write((byte)WorkerMessage.StepStarted); WorkerProtocol.WriteText(writer, id); writer.Flush();
                return await ExecuteSingleAsync(writer, id, true).ConfigureAwait(false);
            }, skipped =>
            {
                writer.Write((byte)WorkerMessage.StepSkipped); WorkerProtocol.WriteText(writer, skipped.TaskId);
                WorkerProtocol.WriteText(writer, skipped.SkipReason); writer.Flush();
            }).ConfigureAwait(false);
            writer.Write((byte)WorkerMessage.SequenceCompleted); writer.Flush();
            return 0;
        }
        private static async Task<MaintenanceTaskResult> ExecuteSingleAsync(BinaryWriter writer, string taskId, bool sequence, bool diagnostic = false)
        {
            var task = ElevatedTaskCatalog.Get(taskId);
            var result = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now };
            var outputLock = new object(); bool connected = true;
            Action<WorkerMessage, string> send = (type, text) =>
            {
                lock (outputLock)
                {
                    if (!connected) return;
                    try { writer.Write((byte)type); WorkerProtocol.WriteText(writer, text); writer.Flush(); }
                    catch (IOException) { connected = false; }
                    catch (ObjectDisposedException) { connected = false; }
                }
            };
            try
            {
                result = await FixedTaskProcess.RunAsync(taskId, start =>
                {
                    result.StartedAt = start; result.CommandStarted = true;
                    lock (outputLock)
                    {
                        try { writer.Write((byte)WorkerMessage.TaskStarted); writer.Write(start.UtcTicks); writer.Flush(); }
                        catch (IOException) { connected = false; }
                    }
                }, text => send(WorkerMessage.StdOutLine, text), text => send(WorkerMessage.StdErrLine, text)).ConfigureAwait(false);
                RepairResultInterpreter.Apply(taskId, result);
                lock (outputLock)
                {
                    if (connected) { writer.Write((byte)(sequence ? WorkerMessage.StepCompleted : WorkerMessage.TaskCompleted)); writer.Write(result.ExitCode.Value); writer.Write(result.StartedAt.UtcTicks); writer.Write(result.FinishedAt.UtcTicks); writer.Flush(); }
                }
                if (!connected) throw new IOException("Conexión IPC interrumpida.");
                return result;
            }
            catch (Exception ex)
            {
                if (diagnostic)
                {
                    result.ExecutionStatus = ExecutionStatus.Failed; result.FindingStatus = FindingStatus.Unknown;
                    result.FinishedAt = DateTimeOffset.Now; result.Duration = result.FinishedAt - result.StartedAt;
                    lock (outputLock)
                    {
                        if (!connected) throw;
                        writer.Write((byte)WorkerMessage.DiagnosticStepFailed); writer.Write(result.CommandStarted);
                        writer.Write(result.StartedAt.UtcTicks); writer.Write(result.FinishedAt.UtcTicks);
                        WorkerProtocol.WriteText(writer, ex.GetType().Name + ": " + ex.Message); writer.Flush();
                    }
                    return result;
                }
                send(WorkerMessage.TaskFailed, ex.GetType().Name + ": " + ex.Message);
                if (sequence) throw;
                result.ExecutionStatus = ExecutionStatus.Failed; return result;
            }
        }
    }
}
