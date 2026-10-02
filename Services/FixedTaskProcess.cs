using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using WinSereno.Models;
namespace WinSereno.Services
{
    internal static class FixedTaskProcess
    {
        [DllImport("kernel32.dll")] private static extern uint GetOEMCP();
        [DllImport("kernel32.dll")] private static extern uint GetACP();
        [DllImport("kernel32.dll")] private static extern uint GetConsoleOutputCP();
        internal static Encoding NetshLegacyEncoding()
        {
            uint codePage = GetConsoleOutputCP();
            return Encoding.GetEncoding((int)(codePage == 0 || codePage == 65001 ? GetOEMCP() : codePage));
        }
        // CHKDSK writes ANSI text to redirected pipes without a console; OEM decoding corrupts its accents.
        internal static Encoding ChkdskOutputEncoding() => Encoding.GetEncoding((int)GetACP());
        // Accept only an allowlisted ID; resolve executable and arguments again at the execution boundary.
        public static async Task<MaintenanceTaskResult> RunAsync(string taskId, Action<DateTimeOffset> started, Action<string> output, Action<string> error)
        {
            var task = ElevatedTaskCatalog.Get(taskId);
            if (task.TaskType != TaskType.Command) throw new ArgumentException("La tarea no es un comando individual.");
            bool netsh = taskId == ElevatedTaskCatalog.ResetWinsockId || taskId == ElevatedTaskCatalog.ResetTcpIpId;
            return await RunCommandAsync(task, taskId == ElevatedTaskCatalog.SfcId || taskId == ElevatedTaskCatalog.SfcVerifyOnlyId ? Encoding.Unicode : taskId == ElevatedTaskCatalog.ChkdskId ? ChkdskOutputEncoding() : taskId == ElevatedTaskCatalog.FlushDnsId ? Encoding.GetEncoding((int)GetOEMCP()) : netsh ? NetshLegacyEncoding() : null, started, output, error, netsh).ConfigureAwait(false);
        }
        internal static async Task<MaintenanceTaskResult> RunDhcpCommandAsync(DhcpRenewalService service, DhcpRenewalPlan plan, int ordinal, Action<DateTimeOffset> started, Action<string> output, Action<string> error)
        {
            service.ValidateCommandTarget(plan, ordinal);
            var command = DhcpRenewalService.Command(plan, ordinal);
            return await RunCommandAsync(command, Encoding.GetEncoding((int)GetOEMCP()), started, output, error).ConfigureAwait(false);
        }
        internal static async Task<MaintenanceTaskResult> RunRestartCommandAsync(AdapterRestartService service, RestartAdapter adapter, int attempt,
            Action<DateTimeOffset> started, Action<string> output, Action<string> error)
        {
            service.ValidateTarget(adapter, attempt);
            return await RunCommandAsync(AdapterRestartService.Command(adapter, attempt), NetshLegacyEncoding(), started, output, error, true).ConfigureAwait(false);
        }
        private static async Task<MaintenanceTaskResult> RunCommandAsync(MaintenanceTask task, Encoding encoding, Action<DateTimeOffset> started, Action<string> output, Action<string> error, bool netsh = false)
        {
            if (!File.Exists(task.Command)) throw new FileNotFoundException("La herramienta no existe en el directorio del sistema.");
            var stdout = new StringBuilder(); var stderr = new StringBuilder();
            using (var process = new Process { StartInfo = new ProcessStartInfo(task.Command, task.Arguments) {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(task.Command), StandardOutputEncoding = encoding, StandardErrorEncoding = encoding } })
            {
                if (!process.Start()) throw new InvalidOperationException("La herramienta no pudo iniciarse.");
                var start = DateTimeOffset.Now; started(start);
                var lines = task.Id == ElevatedTaskCatalog.SfcId || task.Id == ElevatedTaskCatalog.SfcVerifyOnlyId ? new OutputLineBuffer(output) : null;
                Action<string> receiveOutput = text => {
                    stdout.Append(text);
                    if (lines != null) lines.Append(text); else output(text);
                };
                Action<string> receiveError = text => { stderr.Append(text); error(text); };
                var outTask = netsh ? NetshOutputReader.PumpAsync(process.StandardOutput.BaseStream, encoding, receiveOutput) :
                    PumpAsync(process.StandardOutput, receiveOutput, lines == null ? null : (Action)lines.Complete);
                var errTask = netsh ? NetshOutputReader.PumpAsync(process.StandardError.BaseStream, encoding, receiveError) :
                    PumpAsync(process.StandardError, receiveError);
                await Task.WhenAll(outTask, errTask, Task.Run(() => process.WaitForExit())).ConfigureAwait(false);
                var end = DateTimeOffset.Now;
                return new MaintenanceTaskResult { StartedAt = start, FinishedAt = end, Duration = end - start,
                    ExitCode = process.ExitCode, CommandStarted = true, StdOut = stdout.ToString(), StdErr = stderr.ToString() };
            }
        }
        private static async Task PumpAsync(StreamReader stream, Action<string> received, Action completed = null)
        {
            var buffer = new char[1024]; int count;
            while ((count = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0) received(new string(buffer, 0, count));
            completed?.Invoke();
        }
    }
}
