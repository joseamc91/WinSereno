using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WinSereno.Services
{
    internal enum WorkerMessage : byte { WorkerStarted = 1, TaskStarted, StdOutLine, StdErrLine, TaskCompleted, TaskFailed, StepStarted, StepCompleted, StepSkipped, SequenceCompleted, DhcpStepStarted, DhcpStepCompleted, DhcpStepSkipped, DhcpCompleted, RestartStepStarted, RestartStepCompleted, RestartNote, RestartCompleted, WindowsTempAnalyzed, WindowsTempCleanupProgress, WindowsTempCleaned, CleanupBatchCounters, DiagnosticStepFailed }
    internal static class WorkerProtocol
    {
        public const int HandshakeTimeout = 15000;
        public static void WriteText(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
            if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Mensaje IPC demasiado grande.");
            writer.Write(bytes.Length); writer.Write(bytes);
        }
        public static string ReadText(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > 1024 * 1024) throw new InvalidDataException("Longitud IPC inválida.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }
        public static async Task<T> WithTimeout<T>(Task<T> pending, PipeStream pipe)
        {
            if (await Task.WhenAny(pending, Task.Delay(HandshakeTimeout)).ConfigureAwait(false) != pending)
            {
                pipe.Dispose();
                _ = pending.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                throw new TimeoutException("El worker no completó el handshake IPC.");
            }
            return await pending.ConfigureAwait(false);
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);
    }
}
