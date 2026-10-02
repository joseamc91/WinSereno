using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinSereno.Models;

namespace WinSereno.Services
{
    public sealed class MaintenanceTaskRunner : IMaintenanceTaskRunner, ICleanupAnalysisRunner
    {
        private readonly CleanupBatchExecutor batch;
        private readonly RecycleBinCleanupService recycleBin;
        private readonly MockMaintenanceTaskRunner mock;
        private readonly OperationCoordinator operations;
        private readonly ISessionLogger logger;
        private readonly System.Windows.Threading.Dispatcher dispatcher;
        public TaskProgress Current { get; private set; } = new TaskProgress { State = RunnerState.Idle };
        public bool IsActive => operations.IsActive && Current.State == RunnerState.Running;
        public event EventHandler<TaskProgress> ProgressChanged;
        public MaintenanceTaskRunner(ISessionLogger logger, OperationCoordinator operations, RecycleBinCleanupService recycleBin = null, CleanupBatchExecutor batch = null)
        {
            this.recycleBin = recycleBin ?? new RecycleBinCleanupService(logger);
            this.batch = batch ?? new CleanupBatchExecutor(logger, this.recycleBin);
            this.logger = logger; this.operations = operations; dispatcher = System.Windows.Application.Current.Dispatcher;
            mock = new MockMaintenanceTaskRunner(logger, operations);
            mock.ProgressChanged += (s, p) => Publish(p);
        }
        private void Publish(TaskProgress progress)
        {
            Current = progress;
            if (dispatcher.CheckAccess()) ProgressChanged?.Invoke(this, progress);
            else dispatcher.BeginInvoke(new Action(() => ProgressChanged?.Invoke(this, progress)));
        }
        private async Task<MaintenanceTaskResult> RunUserTempAsync(MaintenanceTask task)
        {
            using (operations.Begin("Limpieza de temporales del usuario", false))
            {
                var result = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now };
                var watch = Stopwatch.StartNew(); string last = "Comprobando temporales...";
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                Action publish = () => Publish(new TaskProgress { CurrentTask = task, State = RunnerState.Running, StartedAt = result.StartedAt,
                    Elapsed = watch.Elapsed, LastRelevantLine = last, StdOut = last });
                logger.Write("Confirmación aceptada | TaskId=" + UserTempCleanupService.TaskId + " | Sin UAC ni procesos externos"); logger.TaskStarted(task);
                publish(); timer.Tick += (s, e) => publish(); timer.Start();
                try
                {
                    var progress = new Progress<string>(text => last = text);
                    var cleanup = await Task.Run(() => new UserTempCleanupService(logger).Execute(task, text => ((IProgress<string>)progress).Report(text)));
                    result.UserTempCleanup = cleanup;
                    result.ExecutionStatus = cleanup.Status == UserTempCleanupStatus.Failed ? ExecutionStatus.Failed : ExecutionStatus.Success;
                    result.FindingStatus = cleanup.Status == UserTempCleanupStatus.Failed ? FindingStatus.Unknown : cleanup.Status == UserTempCleanupStatus.Partial ? FindingStatus.PartiallyCompleted : FindingStatus.Completed;
                    string label = cleanup.Status == UserTempCleanupStatus.Failed ? "No se pudo completar la limpieza" : cleanup.Status == UserTempCleanupStatus.Partial ? "Limpieza completada parcialmente" : cleanup.Status == UserTempCleanupStatus.NothingToClean ? "Nada que limpiar" : "Limpieza completada";
                    result.UserSummary = label + " · " + cleanup.Summary.Substring(cleanup.Summary.IndexOf('·') + 2);
                    result.StdOut = result.UserSummary; result.StdErr = cleanup.TechnicalDetails;
                }
                catch (Exception ex) { result.ExecutionStatus = ExecutionStatus.Failed; result.UserSummary = "No se pudo completar la limpieza. Consulta el log."; logger.Write("Error limpieza TEMP: " + ex); }
                finally
                {
                    timer.Stop(); result.FinishedAt = DateTimeOffset.Now; result.Duration = watch.Elapsed; logger.TaskFinished(task, result);
                    Publish(new TaskProgress { CurrentTask = task, State = RunnerState.Completed, StartedAt = result.StartedAt, Elapsed = result.Duration, LastRelevantLine = result.UserSummary, StdOut = result.StdOut, Result = result });
                }
                return result;
            }
        }
        private async Task<MaintenanceTaskResult> RunThumbnailsAsync(MaintenanceTask task)
        {
            using (operations.Begin("Limpieza de caché de miniaturas", false))
            {
                var result = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now };
                var watch = Stopwatch.StartNew(); string last = "Comprobando archivos thumbcache_*.db...";
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                Action publish = () => Publish(new TaskProgress { CurrentTask = task, State = RunnerState.Running, StartedAt = result.StartedAt,
                    Elapsed = watch.Elapsed, LastRelevantLine = last, StdOut = last });
                logger.Write("Confirmación aceptada | TaskId=" + ThumbnailsCleanupService.TaskId + " | Sin UAC ni procesos externos"); logger.TaskStarted(task);
                publish(); timer.Tick += (s, e) => publish(); timer.Start();
                try
                {
                    var progress = new Progress<string>(text => last = text);
                    result = await ThumbnailsCleanupService.RunAsync(task, logger, text => ((IProgress<string>)progress).Report(text));
                }
                catch (Exception ex) { result.ExecutionStatus = ExecutionStatus.Failed; result.UserSummary = "No se pudo completar la limpieza de miniaturas. Consulta el log."; logger.Write("Error limpieza miniaturas: " + ex); }
                finally
                {
                    timer.Stop(); result.FinishedAt = DateTimeOffset.Now; result.Duration = watch.Elapsed; logger.TaskFinished(task, result);
                    Publish(new TaskProgress { CurrentTask = task, State = RunnerState.Completed, StartedAt = result.StartedAt, Elapsed = result.Duration,
                        LastRelevantLine = result.UserSummary, StdOut = result.StdOut, StdErr = result.StdErr, Result = result });
                }
                return result;
            }
        }
        private async Task<MaintenanceTaskResult> RunRecycleBinAsync(MaintenanceTask task)
        {
            using (operations.Begin("Vaciar Papelera", false))
            {
                var result = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now };
                var watch = Stopwatch.StartNew(); string last = "Consultando Papelera...";
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                Action publish = () => Publish(new TaskProgress { CurrentTask = task, State = RunnerState.Running, StartedAt = result.StartedAt,
                    Elapsed = watch.Elapsed, LastRelevantLine = last, StdOut = last });
                logger.Write("Confirmación aceptada | TaskId=" + RecycleBinCleanupService.TaskId + " | Sin UAC ni confirmación del Shell"); logger.TaskStarted(task);
                publish(); timer.Tick += (s, e) => publish(); timer.Start();
                try { var progress = new Progress<string>(text => last = text); result = await recycleBin.RunAsync(text => ((IProgress<string>)progress).Report(text)); }
                catch (Exception ex) { result.ExecutionStatus = ExecutionStatus.Failed; result.UserSummary = "No se pudo completar el vaciado de Papelera. Consulta el log."; logger.Write("Error Papelera: " + ex); }
                finally
                {
                    timer.Stop(); result.FinishedAt = DateTimeOffset.Now; result.Duration = watch.Elapsed; logger.TaskFinished(task, result);
                    Publish(new TaskProgress { CurrentTask = task, State = RunnerState.Completed, StartedAt = result.StartedAt, Elapsed = result.Duration,
                        LastRelevantLine = result.UserSummary, StdOut = result.StdOut, StdErr = result.StdErr, Result = result });
                }
                return result;
            }
        }
        private async Task<MaintenanceTaskResult> RunCleanupBatchAsync(MaintenanceTask task)
        {
            CleanupBatchExecutor.Categories(task.CleanupSelection);
            using (operations.Begin("Limpiar seleccionados", false))
            {
                logger.Write("Confirmación aceptada | Limpiar seleccionados | Selección=" + task.CleanupSelection); logger.TaskStarted(task);
                TaskProgress latest = new TaskProgress { CurrentTask = task, State = RunnerState.Running, StartedAt = DateTimeOffset.Now, StepLabel = "Preparando limpieza..." };
                var watch = Stopwatch.StartNew();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                timer.Tick += (s, e) => { latest.Elapsed = watch.Elapsed; Publish(latest); }; timer.Start(); Publish(latest);
                MaintenanceTaskResult result;
                try
                {
                    bool completed = false;
                    var progress = new Progress<TaskProgress>(value => { if (!completed) { latest = value; Publish(value); } });
                    result = await batch.RunAsync(task, value => ((IProgress<TaskProgress>)progress).Report(value)); completed = true;
                }
                finally { timer.Stop(); }
                logger.TaskFinished(task, result);
                Publish(new TaskProgress { CurrentTask = task, State = RunnerState.Completed, Result = result, Elapsed = result.Duration,
                    LastRelevantLine = result.UserSummary, StdOut = result.StdOut, StdErr = result.StdErr });
                return result;
            }
        }
        public bool RequestCancellation() => mock.IsActive && mock.RequestCancellation();
        public Task WaitForIdleAsync() => operations.WaitForIdleAsync();
        public async Task<MaintenanceTaskResult> RunAsync(MaintenanceTask requested)
        {
            if (requested.Id == ElevatedTaskCatalog.DiagnosticIntegrityId)
                throw new InvalidOperationException("La integridad de Diagnóstico requiere su operación global y no se ejecuta como acción independiente.");
            if (requested.Id == CleanupBatchExecutor.TaskId) return await RunCleanupBatchAsync(requested);
            if (requested.Id == RecycleBinCleanupService.TaskId) return await RunRecycleBinAsync(requested);
            if (requested.Id == ThumbnailsCleanupService.TaskId) return await RunThumbnailsAsync(requested);
            if (requested.Id == UserTempCleanupService.TaskId) return await RunUserTempAsync(requested);
            if (requested.Id == ElevatedTaskCatalog.RenewDhcpId) return await new DhcpMaintenanceTaskRunner(logger, operations, dispatcher, Publish).RunAsync(requested);
            if (requested.IsMock) return await mock.RunAsync(requested);
            bool restart = requested.Id == ElevatedTaskCatalog.RestartAdapterId;
            var adapter = requested.RestartAdapter;
            var task = restart ? AdapterRestartService.PrepareTask(adapter) : ElevatedTaskCatalog.Get(requested.Id); // Ignore all caller-supplied execution fields.
            if (task.Id == ElevatedTaskCatalog.ResetTcpIpId)
            {
                if (requested.TcpIpApproval == null) throw new InvalidOperationException("TCP/IP requiere el preflight y sus confirmaciones antes de solicitar UAC.");
                requested.TcpIpApproval.Consume(); task.TcpIpApproval = requested.TcpIpApproval;
            }
            return await RunElevatedCoreAsync(task, restart, adapter, null, null, null);
        }
        public async Task<MaintenanceTaskResult> RunCleanupAnalysisAsync(OperationLease lease, Action<TaskProgress> progress)
        {
            if (lease == null || !operations.Owns(lease) || lease.Name != "Análisis de Limpieza" || progress == null)
                throw new InvalidOperationException("El análisis administrativo requiere la operación global de Limpieza activa.");
            lease.Token.ThrowIfCancellationRequested();
            lease.SetCancelable(false);
            return await RunElevatedCoreAsync(ElevatedTaskCatalog.Get(ElevatedTaskCatalog.WindowsTempAnalyzeId), false, null, lease, null, progress);
        }
        internal async Task<MaintenanceTaskResult> RunDiagnosticAsync(OperationLease lease, Func<Task> normalChecks, Action<TaskProgress> progress)
        {
            if (lease == null || !operations.Owns(lease) || lease.Name != "Diagnóstico") throw new InvalidOperationException("Se requiere la operación global de Diagnóstico activa.");
            if (normalChecks == null || progress == null) throw new ArgumentNullException("El diagnóstico requiere sus callbacks internos.");
            bool normalRan = false;
            lease.SetCancelable(false);
            Func<Task> beforeIntegrity = async () => {
                normalRan = true; lease.SetCancelable(true);
                try { await normalChecks(); lease.Token.ThrowIfCancellationRequested(); }
                finally { lease.SetCancelable(false); }
            };
            try { return await RunElevatedCoreAsync(ElevatedTaskCatalog.Get(ElevatedTaskCatalog.DiagnosticIntegrityId), false, null, lease, beforeIntegrity, progress); }
            finally {
                // A rejected UAC/failed connection must not suppress the ordinary read-only checks.
                if (!normalRan) await beforeIntegrity();
            }
        }
        private async Task<MaintenanceTaskResult> RunElevatedCoreAsync(MaintenanceTask task, bool restart, RestartAdapter adapter,
            OperationLease externalLease, Func<Task> normalChecks, Action<TaskProgress> internalProgress)
        {
            Action<TaskProgress> report = internalProgress ?? Publish;
            using (var lease = externalLease == null ? operations.Begin(task.Name, false) : null)
            {
                var watch = Stopwatch.StartNew();
                var stdout = new StringBuilder(); var stderr = new StringBuilder();
                var result = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now };
                bool diagnostic = task.Id == ElevatedTaskCatalog.DiagnosticIntegrityId;
                bool sequence = task.Id == ElevatedTaskCatalog.CompleteId || diagnostic;
                var sequenceIds = diagnostic ? DiagnosticIntegritySequence.TaskIds : RepairCompleteSequence.TaskIds;
                MaintenanceTask activeTask = task;
                MaintenanceTaskResult stepResult = null;
                var stepOut = new StringBuilder(); var stepErr = new StringBuilder();
                string stepLabel = null;
                double? percentage = null; bool receivedCompletion = false;
                string pendingProgress = "";
                string last = task.RequiresElevation ? "Solicitando permisos de administrador..." : "Ejecutando con permisos normales...";
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                Action publish = () => report(new TaskProgress { CurrentTask = task, State = RunnerState.Running,
                    StartedAt = result.StartedAt, Elapsed = watch.Elapsed, LastRelevantLine = last, StdOut = stdout.ToString(), StdErr = stderr.ToString(), Percentage = percentage, StepLabel = stepLabel });
                publish(); timer.Tick += (s, e) => publish(); timer.Start();
                Process worker = null;
                try
                {
                    logger.Write((diagnostic ? "Solicitud explícita de Analizar este PC" : "Confirmación aceptada") + " | TaskId=" + task.Id);
                    logger.Write("Comando confirmado=" + task.Command + " " + task.Arguments + " | Requiere elevación inicial=" + task.RequiresElevation + " | Cancelable=False");
                    if (task.Id == ElevatedTaskCatalog.FlushDnsId)
                    {
                        logger.Write("Intento sin elevación | TaskId=" + task.Id);
                        result = await FixedTaskProcess.RunAsync(task.Id, began => dispatcher.Invoke(() => {
                            result.StartedAt = began; logger.TaskStarted(task);
                        }), text => dispatcher.Invoke(() => {
                            stdout.Append(text); if (!string.IsNullOrWhiteSpace(text)) last = LastLine(text);
                            logger.Write(task.Id + " stdout: " + text); publish();
                        }), text => dispatcher.Invoke(() => {
                            stderr.Append(text); if (!string.IsNullOrWhiteSpace(text)) last = LastLine(text);
                            logger.Write(task.Id + " stderr: " + text); publish();
                        }));
                        FlushDnsResultParser.Apply(result);
                        if (!FlushDnsResultParser.NeedsElevation(result))
                        { receivedCompletion = true; return result; }
                        logger.Write("Windows denegó permisos; se requiere elevación para " + task.Id);
                        logger.TaskFinished(task, result);
                        stdout.Clear(); stderr.Clear();
                        result = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now };
                        last = "Windows requiere permisos; solicitando elevación..."; publish();
                    }
                    if (restart) await Task.Run(() => new AdapterRestartService(logger).ResolveConfirmed(AdapterRestartService.Fingerprint(adapter)));
                    logger.Write("Solicitud de elevación | mismo WinSereno.exe | TaskId=" + task.Id);
                    string pipeId = Guid.NewGuid().ToString("N");
                    byte[] bytes = new byte[32]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
                    string nonce = BitConverter.ToString(bytes).Replace("-", "");
                    var sid = WindowsIdentity.GetCurrent().User;
                    var acl = new PipeSecurity(); acl.SetAccessRuleProtection(true, false); acl.SetOwner(sid);
                    acl.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
                    acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
                    using (var pipe = new NamedPipeServerStream("WinSereno-" + pipeId, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, acl))
                    {
                        var connection = pipe.WaitForConnectionAsync();
                        worker = await Task.Run(() => Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
                            "--elevated-worker " + task.Id + " " + pipeId + " " + nonce + " " + Process.GetCurrentProcess().Id) {
                            UseShellExecute = true, Verb = "runas", WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory }));
                        if (worker == null) throw new InvalidOperationException("El worker no pudo iniciarse.");
                        await WorkerProtocol.WithTimeout(WaitConnected(connection), pipe);
                        if (!WorkerProtocol.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid) || pid != worker.Id)
                            throw new InvalidDataException("Proceso cliente IPC no autorizado.");
                        using (var reader = new BinaryReader(pipe, Encoding.UTF8, true))
                        using (var writer = new BinaryWriter(pipe, Encoding.UTF8, true))
                        {
                            bool valid = await WorkerProtocol.WithTimeout(Task.Run(() => WorkerProtocol.ReadText(reader) == "WM1" && WorkerProtocol.ReadText(reader) == nonce), pipe);
                            writer.Write(valid); writer.Flush();
                            if (!valid) throw new InvalidDataException("Handshake IPC inválido.");
                            if (restart) { WorkerProtocol.WriteText(writer, AdapterRestartService.Fingerprint(adapter)); writer.Flush(); logger.TaskStarted(task); }
                            if (task.Id == ElevatedTaskCatalog.ResetTcpIpId)
                            {
                                WorkerProtocol.WriteText(writer, task.TcpIpApproval.Fingerprint);
                                writer.Write(task.TcpIpApproval.WarningAccepted); writer.Flush();
                            }
                            int restartAttempt = -1;
                            bool completed = false, started = false;
                            while (!completed)
                            {
                                WorkerMessage message = await Task.Run(() => (WorkerMessage)reader.ReadByte());
                                switch (message)
                                {
                                    case WorkerMessage.WorkerStarted:
                                        logger.Write("Worker elevado autenticado | TaskId=" + task.Id);
                                        if (sequence) logger.TaskStarted(task);
                                        if (diagnostic) {
                                            timer.Stop();
                                            try { await normalChecks(); writer.Write(true); writer.Flush(); }
                                            catch (OperationCanceledException) { writer.Write(false); writer.Flush(); throw; }
                                            finally { timer.Start(); }
                                        }
                                        break;
                                    case WorkerMessage.RestartNote:
                                        if (!restart) throw new InvalidDataException("Nota de reinicio inesperada.");
                                        string restartNote = await Task.Run(() => WorkerProtocol.ReadText(reader));
                                        stdout.AppendLine("[WinSereno] " + restartNote); last = restartNote; logger.Write(task.Id + " | " + restartNote); break;
                                    case WorkerMessage.RestartStepStarted:
                                        if (!restart || stepResult != null) throw new InvalidDataException("Inicio de reinicio inesperado.");
                                        int attempt = await Task.Run(() => reader.ReadInt32());
                                        if (attempt != restartAttempt + 1 || attempt > AdapterRestartPolicy.EnableAttempts) throw new InvalidDataException("Intento fuera de secuencia fija.");
                                        restartAttempt = attempt; activeTask = AdapterRestartService.Command(adapter, attempt);
                                        stepResult = new MaintenanceTaskResult { StartedAt = DateTimeOffset.Now }; stepOut.Clear(); stepErr.Clear();
                                        stepLabel = attempt == 0 ? "Paso 1 de 2 · Deshabilitando " + adapter.Name : "Paso 2 de 2 · Habilitando " + adapter.Name + " · Intento " + attempt;
                                        stdout.AppendLine("\n=== " + activeTask.Command + " " + activeTask.Arguments + " ==="); logger.TaskStarted(activeTask); break;
                                    case WorkerMessage.RestartStepCompleted:
                                        if (!restart || stepResult == null || await Task.Run(() => reader.ReadInt32()) != restartAttempt) throw new InvalidDataException("Resultado de reinicio inesperado.");
                                        bool hasRestartCode = await Task.Run(() => reader.ReadBoolean());
                                        stepResult.ExitCode = hasRestartCode ? (int?)await Task.Run(() => reader.ReadInt32()) : null;
                                        stepResult.StartedAt = new DateTimeOffset(await Task.Run(() => reader.ReadInt64()), TimeSpan.Zero).ToLocalTime();
                                        stepResult.FinishedAt = new DateTimeOffset(await Task.Run(() => reader.ReadInt64()), TimeSpan.Zero).ToLocalTime(); stepResult.Duration = stepResult.FinishedAt - stepResult.StartedAt;
                                        bool commandStarted = await Task.Run(() => reader.ReadBoolean()); var adapterState = (AdapterRestartState)await Task.Run(() => reader.ReadByte());
                                        if (!Enum.IsDefined(typeof(AdapterRestartState), adapterState)) throw new InvalidDataException("Estado de adaptador inválido.");
                                        stepResult.StdOut = stepOut.ToString(); stepResult.StdErr = stepErr.ToString();
                                        AdapterRestartPolicy.ApplyAttempt(restartAttempt, new AdapterRestartAttempt { Result = stepResult, State = adapterState, CommandStarted = commandStarted });
                                        result.SequenceSteps.Add(new SequenceStepResult { TaskId = activeTask.Id, Result = stepResult }); logger.TaskFinished(activeTask, stepResult);
                                        stdout.AppendLine("[Resultado] " + stepResult.UserSummary + " | Duración=" + stepResult.Duration); stepResult = null; break;
                                    case WorkerMessage.RestartCompleted:
                                        if (!restart || stepResult != null || restartAttempt < 0) throw new InvalidDataException("Final de reinicio inesperado.");
                                        result.ExecutionStatus = (ExecutionStatus)await Task.Run(() => reader.ReadByte());
                                        result.FindingStatus = (FindingStatus)await Task.Run(() => reader.ReadByte());
                                        if (!Enum.IsDefined(typeof(ExecutionStatus), result.ExecutionStatus) || !Enum.IsDefined(typeof(FindingStatus), result.FindingStatus)) throw new InvalidDataException("Resultado de reinicio inválido.");
                                        result.UserSummary = await Task.Run(() => WorkerProtocol.ReadText(reader));
                                        if (restartAttempt == 0) { var skip = new SequenceStepResult { TaskId = task.Id + "/enable", WasSkipped = true, SkipReason = "Disable falló; se detuvo el reinicio sin habilitación." }; result.SequenceSteps.Add(skip); logger.Write("Skipped | " + skip.TaskId + " | " + skip.SkipReason); }
                                        receivedCompletion = true; completed = true; break;
                                    case WorkerMessage.StepStarted:
                                        if (!sequence || stepResult != null) throw new InvalidDataException("Paso IPC inesperado.");
                                        string stepId = await Task.Run(() => WorkerProtocol.ReadText(reader));
                                        int index = result.SequenceSteps.Count;
                                        if (index >= sequenceIds.Count || sequenceIds[index] != stepId)
                                            throw new InvalidDataException("Paso fuera de la secuencia fija.");
                                        activeTask = ElevatedTaskCatalog.Get(stepId); stepResult = new MaintenanceTaskResult();
                                        stepOut.Clear(); stepErr.Clear(); percentage = null; pendingProgress = ""; started = false;
                                        stepLabel = "Paso " + (index + 1) + " de " + sequenceIds.Count + " · " + activeTask.Name;
                                        stdout.AppendLine("\n=== " + stepLabel + " ==="); stderr.AppendLine("\n=== " + stepLabel + " ===");
                                        logger.Write(stepLabel + " | TaskId=" + stepId); break;
                                    case WorkerMessage.WindowsTempCleanupProgress:
                                        if (task.Id != ElevatedTaskCatalog.WindowsTempCleanupId || !started) throw new InvalidDataException("Progreso limpieza inesperado.");
                                        last = await Task.Run(() => WorkerProtocol.ReadText(reader)); stdout.Clear(); stdout.AppendLine(last); stepLabel = "Limpieza de temporales de Windows";
                                        if (last.StartsWith("Reanalizando")) stepLabel = "Reanalizando temporales de Windows";
                                        break;
                                    case WorkerMessage.WindowsTempCleaned:
                                        if (task.Id != ElevatedTaskCatalog.WindowsTempCleanupId || !started) throw new InvalidDataException("Final limpieza inesperado.");
                                        await Task.Run(() => WindowsTempCleanupProtocol.ReadInto(reader, result));
                                        WindowsTempCleanupProtocol.Apply(result); stdout.AppendLine(result.UserSummary); stderr.Append(result.StdErr);
                                        logger.Write("Reanálisis posterior dentro del mismo worker | " + WindowsTempCleanupProtocol.AnalysisSummary(result.WindowsTempAnalysis));
                                        receivedCompletion = true; completed = true; break;
                                    case WorkerMessage.WindowsTempAnalyzed:
                                        if (task.Id != ElevatedTaskCatalog.WindowsTempAnalyzeId || !started) throw new InvalidDataException("Resultado administrativo inesperado.");
                                        result.WindowsTempAnalysis = await Task.Run(() => WindowsTempAnalysisProtocol.Read(reader));
                                        WindowsTempAnalysisProtocol.Apply(result);
                                        stdout.Append(result.UserSummary); receivedCompletion = true; completed = true; break;
                                    case WorkerMessage.TaskStarted:
                                        if (started) throw new InvalidDataException("Inicio duplicado.");
                                        var began = new DateTimeOffset(await Task.Run(() => reader.ReadInt64()), TimeSpan.Zero).ToLocalTime(); started = true;
                                        if (sequence) { if (stepResult == null) throw new InvalidDataException("Inicio sin paso."); stepResult.StartedAt = began; stepResult.CommandStarted = true; }
                                        else { result.StartedAt = began; result.CommandStarted = true; }
                                        logger.TaskStarted(activeTask); last = "Ejecutando " + activeTask.Name + "..."; break;
                                    case WorkerMessage.StdOutLine:
                                        string line = await Task.Run(() => WorkerProtocol.ReadText(reader)); stdout.Append(line); if (!string.IsNullOrWhiteSpace(line)) last = LastLine(line);
                                        if (sequence || restart) stepOut.Append(line);
                                        logger.Write(activeTask.Id + " stdout: " + line);
                                        pendingProgress += line;
                                        string[] segments = pendingProgress.Split(new[] { '\r', '\n' });
                                        foreach (string segment in segments)
                                        { var value = ToolProgressParser.Parse(activeTask.Id == ElevatedTaskCatalog.SfcVerifyOnlyId ? ElevatedTaskCatalog.SfcId : activeTask.Id, segment); if (value.HasValue && value != percentage) { percentage = value; logger.Write("Progreso real=" + value.Value + "% | TaskId=" + activeTask.Id); } }
                                        pendingProgress = segments[segments.Length - 1]; break;
                                    case WorkerMessage.StdErrLine:
                                        string error = await Task.Run(() => WorkerProtocol.ReadText(reader)); stderr.Append(error); if (!string.IsNullOrWhiteSpace(error)) last = LastLine(error);
                                        if (sequence || restart) stepErr.Append(error);
                                        logger.Write(activeTask.Id + " stderr: " + error); break;
                                    case WorkerMessage.StepCompleted:
                                    case WorkerMessage.TaskCompleted:
                                        if (!started) throw new InvalidDataException("Finalización sin inicio.");
                                        if ((message == WorkerMessage.StepCompleted) != sequence) throw new InvalidDataException("Finalización IPC inesperada.");
                                        var ended = sequence ? stepResult : result;
                                        ended.ExitCode = await Task.Run(() => reader.ReadInt32());
                                        ended.StartedAt = new DateTimeOffset(await Task.Run(() => reader.ReadInt64()), TimeSpan.Zero).ToLocalTime();
                                        ended.FinishedAt = new DateTimeOffset(await Task.Run(() => reader.ReadInt64()), TimeSpan.Zero).ToLocalTime();
                                        ended.Duration = ended.FinishedAt - ended.StartedAt;
                                        if (sequence)
                                        {
                                            ended.StdOut = stepOut.ToString(); ended.StdErr = stepErr.ToString(); RepairResultInterpreter.Apply(activeTask.Id, ended);
                                            result.SequenceSteps.Add(new SequenceStepResult { TaskId = activeTask.Id, Result = ended });
                                            logger.TaskFinished(activeTask, ended); stepResult = null; started = false; percentage = null;
                                        }
                                        else { receivedCompletion = true; completed = true; }
                                        break;
                                    case WorkerMessage.DiagnosticStepFailed:
                                        if (!diagnostic || stepResult == null) throw new InvalidDataException("Fallo de paso inesperado.");
                                        stepResult.CommandStarted = await Task.Run(() => reader.ReadBoolean());
                                        stepResult.StartedAt = new DateTimeOffset(await Task.Run(() => reader.ReadInt64()), TimeSpan.Zero).ToLocalTime();
                                        stepResult.FinishedAt = new DateTimeOffset(await Task.Run(() => reader.ReadInt64()), TimeSpan.Zero).ToLocalTime();
                                        string failure = await Task.Run(() => WorkerProtocol.ReadText(reader));
                                        stepResult.Duration = stepResult.FinishedAt - stepResult.StartedAt;
                                        stepResult.ExecutionStatus = ExecutionStatus.Failed; stepResult.FindingStatus = FindingStatus.Unknown;
                                        stepResult.UserSummary = "No se pudo ejecutar o confirmar la comprobación.";
                                        stepResult.StdOut = stepOut.ToString(); stepResult.StdErr = stepErr.ToString() + failure;
                                        result.SequenceSteps.Add(new SequenceStepResult { TaskId = activeTask.Id, Result = stepResult });
                                        logger.Write("Error técnico de integridad | " + activeTask.Id + " | " + failure); logger.TaskFinished(activeTask, stepResult);
                                        stepResult = null; started = false; percentage = null; break;
                                    case WorkerMessage.StepSkipped:
                                        if (!sequence || diagnostic || stepResult != null) throw new InvalidDataException("Omisión IPC inesperada.");
                                        string skippedId = await Task.Run(() => WorkerProtocol.ReadText(reader));
                                        if (result.SequenceSteps.Count >= sequenceIds.Count || sequenceIds[result.SequenceSteps.Count] != skippedId)
                                            throw new InvalidDataException("Omisión fuera de secuencia.");
                                        string reason = await Task.Run(() => WorkerProtocol.ReadText(reader));
                                        result.SequenceSteps.Add(new SequenceStepResult { TaskId = skippedId, WasSkipped = true, SkipReason = reason });
                                        logger.Write("Paso Skipped | TaskId=" + skippedId + " | Motivo=" + reason);
                                        stdout.AppendLine("\nOmitido " + ElevatedTaskCatalog.Get(skippedId).Name + ": " + reason); break;
                                    case WorkerMessage.SequenceCompleted:
                                        if (!sequence || stepResult != null || result.SequenceSteps.Count != sequenceIds.Count) throw new InvalidDataException("Secuencia incompleta.");
                                        receivedCompletion = true; completed = true; break;
                                    case WorkerMessage.TaskFailed: throw new IOException("Worker: " + await Task.Run(() => WorkerProtocol.ReadText(reader)));
                                    default: throw new InvalidDataException("Mensaje IPC desconocido.");
                                }
                                publish();
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (diagnostic)
                {
                    result.ExecutionStatus = ExecutionStatus.Cancelled;
                    logger.Write("Diagnóstico cancelado antes de iniciar integridad.");
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    result.ExecutionStatus = ExecutionStatus.Cancelled;
                    logger.Write("Elevación cancelada por el usuario | TaskId=" + task.Id);
                }
                catch (Exception ex)
                {
                    result.ExecutionStatus = ExecutionStatus.Failed;
                    logger.Write("Error técnico " + task.Id + " | " + ex);
                    last = "No se pudo completar la comunicación o iniciar la comprobación.";
                }
                finally
                {
                    // If IPC breaks, wait for this non-cancelable worker; never kill DISM or release the global slot early.
                    if (worker != null)
                    {
                        try { if (!worker.HasExited) { last = "Esperando a que termine el worker no cancelable..."; await Task.Run(() => worker.WaitForExit()); } }
                        catch (Exception ex) { logger.Write("No se pudo observar el cierre del worker: " + ex); }
                        worker.Dispose();
                    }
                    timer.Stop(); watch.Stop();
                    result.StdOut = stdout.ToString(); result.StdErr = stderr.ToString();
                    if (result.FinishedAt == default(DateTimeOffset)) { result.FinishedAt = DateTimeOffset.Now; result.Duration = watch.Elapsed; }
                    if (!receivedCompletion) result.ExitCode = null;
                    if (sequence)
                    {
                        if (stepResult != null)
                        {
                            stepResult.ExecutionStatus = ExecutionStatus.Failed; stepResult.UserSummary = "No se pudo confirmar el resultado del paso por un fallo del worker o IPC.";
                            stepResult.StdOut = stepOut.ToString(); stepResult.StdErr = stepErr.ToString(); stepResult.FinishedAt = DateTimeOffset.Now;
                            stepResult.Duration = stepResult.StartedAt == default(DateTimeOffset) ? TimeSpan.Zero : stepResult.FinishedAt - stepResult.StartedAt;
                            result.SequenceSteps.Add(new SequenceStepResult { TaskId = activeTask.Id, Result = stepResult }); logger.TaskFinished(activeTask, stepResult);
                        }
                        int recorded = result.SequenceSteps.Count;
                        if (diagnostic) DiagnosticIntegritySequence.CompleteMissing(result);
                        else RepairCompleteSequence.MarkUnexecuted(result, result.ExecutionStatus == ExecutionStatus.Cancelled ? "Elevación cancelada por el usuario; la secuencia no se inició." : "La secuencia se interrumpió por un fallo técnico.");
                        for (int i = recorded; i < result.SequenceSteps.Count; i++)
                        { var skip = result.SequenceSteps[i]; logger.Write("Paso Skipped | TaskId=" + skip.TaskId + " | Motivo=" + skip.SkipReason); }
                        if (diagnostic) DiagnosticIntegritySequence.Summarize(result); else RepairCompleteSequence.Summarize(result);
                        if (!receivedCompletion && result.ExecutionStatus == ExecutionStatus.Success)
                        { result.ExecutionStatus = ExecutionStatus.Failed; result.FindingStatus = FindingStatus.Unknown; result.UserSummary = "No se pudo confirmar la finalización de la secuencia por un fallo del worker o IPC."; }
                    }
                    else if (restart)
                    {
                        result.ExitCode = null;
                        if (!receivedCompletion)
                        {
                            result.FindingStatus = FindingStatus.Unknown;
                            result.UserSummary = result.ExecutionStatus == ExecutionStatus.Cancelled ? "Elevación cancelada por el usuario; no se reinició el adaptador." : worker == null ? "No se inició el reinicio de «" + adapter.Name + "». No se ejecutaron comandos; la selección cambió o no pudo iniciarse la elevación. Consulta el log." :
                                "No se pudo confirmar el reinicio de «" + adapter.Name + "». Si el worker llegó a deshabilitarlo, intenta recuperarlo aunque se pierda IPC. Comprueba su estado localmente y habilítalo en Configuración > Red e Internet > Configuración de red avanzada si es necesario.";
                            if (worker == null && result.SequenceSteps.Count == 0)
                            {
                                foreach (string phase in new[] { "disable", "enable" })
                                { var skip = new SequenceStepResult { TaskId = task.Id + "/" + phase, WasSkipped = true, SkipReason = "No se inició el worker: cancelación, selección cambiada o fallo de elevación." }; result.SequenceSteps.Add(skip); logger.Write("Skipped | " + skip.TaskId + " | " + skip.SkipReason); }
                            }
                            if (stepResult != null) { stepResult.ExecutionStatus = ExecutionStatus.Failed; stepResult.StdOut = stepOut.ToString(); stepResult.StdErr = stepErr.ToString(); stepResult.FinishedAt = DateTimeOffset.Now; stepResult.Duration = stepResult.FinishedAt - stepResult.StartedAt; stepResult.UserSummary = "Resultado no confirmado por fallo IPC."; result.SequenceSteps.Add(new SequenceStepResult { TaskId = activeTask.Id, Result = stepResult }); logger.TaskFinished(activeTask, stepResult); }
                        }
                    }
                    else if (task.Id == ElevatedTaskCatalog.WindowsTempCleanupId) WindowsTempCleanupProtocol.Apply(result);
                    else if (task.Id == ElevatedTaskCatalog.WindowsTempAnalyzeId) WindowsTempAnalysisProtocol.Apply(result);
                    else RepairResultInterpreter.Apply(task.Id, result);
                    try { logger.TaskFinished(task, result); }
                    finally { report(new TaskProgress { CurrentTask = task, State = RunnerState.Completed, StartedAt = result.StartedAt,
                        Elapsed = result.Duration, LastRelevantLine = result.UserSummary, StdOut = result.StdOut, StdErr = result.StdErr, Percentage = percentage, Result = result }); }
                }
                return result;
            }
        }
        private static string LastLine(string text)
        {
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? text : lines[lines.Length - 1].Trim();
        }
        private static async Task<bool> WaitConnected(Task pending) { await pending.ConfigureAwait(false); return true; }
    }
}
