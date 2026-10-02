using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using WinSereno.Models;
namespace WinSereno.Services
{
    // The elevated session accepts only one fixed Windows Temp operation and a close request.
    internal sealed class CleanupWindowsSession : ICleanupWindowsSession
    {
        private NamedPipeServerStream pipe;
        private Process worker;
        private BinaryReader reader;
        private BinaryWriter writer;
        private bool executed;
        public static async Task<ICleanupWindowsSession> OpenAsync(ISessionLogger logger)
        {
            var session = new CleanupWindowsSession();
            try
            {
                string id = Guid.NewGuid().ToString("N"); byte[] bytes = new byte[32]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
                string nonce = BitConverter.ToString(bytes).Replace("-", ""); var sid = WindowsIdentity.GetCurrent().User;
                var acl = new PipeSecurity(); acl.SetAccessRuleProtection(true, false); acl.SetOwner(sid);
                acl.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
                acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
                session.pipe = new NamedPipeServerStream("WinSereno-" + id, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, acl);
                var connected = session.pipe.WaitForConnectionAsync();
                logger?.Write("Solicitud de elevación única antes del borrado | TaskId=" + CleanupBatchExecutor.TaskId);
                session.worker = await Task.Run(() => Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
                    "--elevated-worker " + CleanupBatchExecutor.TaskId + " " + id + " " + nonce + " " + Process.GetCurrentProcess().Id)
                    { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory }));
                if (session.worker == null) throw new IOException("No se pudo iniciar el worker.");
                await WorkerProtocol.WithTimeout(Task.Run(async () => { await connected; return true; }), session.pipe);
                if (!WorkerProtocol.GetNamedPipeClientProcessId(session.pipe.SafePipeHandle, out uint pid) || pid != session.worker.Id) throw new InvalidDataException("Worker no autorizado.");
                session.reader = new BinaryReader(session.pipe, Encoding.UTF8, true); session.writer = new BinaryWriter(session.pipe, Encoding.UTF8, true);
                bool valid = await WorkerProtocol.WithTimeout(Task.Run(() => WorkerProtocol.ReadText(session.reader) == "WM1" && WorkerProtocol.ReadText(session.reader) == nonce), session.pipe);
                session.writer.Write(valid); session.writer.Flush(); if (!valid) throw new InvalidDataException("Handshake inválido.");
                if (await WorkerProtocol.WithTimeout(Task.Run(() => session.reader.ReadByte()), session.pipe) != (byte)WorkerMessage.WorkerStarted) throw new InvalidDataException("Inicio de worker inválido.");
                logger?.Write("Worker autenticado; listo para Windows Temp. Todavía no se inició ningún borrado.");
                return session;
            }
            catch { await session.CloseAsync(); throw; }
        }
        public async Task<MaintenanceTaskResult> RunWindowsAsync(Action<CleanupCounters> progress)
        {
            if (executed) throw new InvalidOperationException("Windows Temp ya se ejecutó en esta sesión."); executed = true;
            try
            {
                writer.Write((byte)1); writer.Flush();
                while (true)
                {
                    var type = (WorkerMessage)await Task.Run(() => reader.ReadByte());
                    if (type == WorkerMessage.CleanupBatchCounters)
                    {
                        var counts = await Task.Run(() => new CleanupCounters { ReleasedBytes = ReadCount(), RemovedElements = ReadCount(), SkippedElements = ReadCount() });
                        progress?.Invoke(counts);
                    }
                    else if (type == WorkerMessage.WindowsTempCleaned)
                    {
                        var result = new MaintenanceTaskResult(); await Task.Run(() => WindowsTempCleanupProtocol.ReadInto(reader, result));
                        WindowsTempCleanupProtocol.Apply(result); return result;
                    }
                    else throw new InvalidDataException("Mensaje de sesión administrativa no permitido.");
                }
            }
            catch { await CloseAsync(); throw; } // Wait for a running Windows operation before continuing other categories.
        }
        public async Task<CleanupCategoryResult> ReanalyzeWindowsAsync()
        {
            if (!executed) throw new InvalidOperationException("No se inició Windows Temp.");
            writer.Write((byte)3); writer.Flush();
            if ((WorkerMessage)await Task.Run(() => reader.ReadByte()) != WorkerMessage.WindowsTempAnalyzed) throw new InvalidDataException("Reanálisis inválido.");
            return await Task.Run(() => WindowsTempAnalysisProtocol.Read(reader));
        }
        private long ReadCount() { long value = reader.ReadInt64(); if (value < 0) throw new InvalidDataException("Contador negativo."); return value; }
        public async Task CloseAsync()
        {
            try { if (writer != null) { writer.Write((byte)2); writer.Flush(); } } catch (IOException) { } catch (ObjectDisposedException) { }
            finally
            {
                pipe?.Dispose();
                if (worker != null) { try { if (!worker.HasExited) await Task.Run(() => worker.WaitForExit()); } finally { worker.Dispose(); worker = null; } }
            }
        }
    }
}
