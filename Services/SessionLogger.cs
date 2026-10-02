using System;
using System.IO;
using System.Text;
using WinSereno.Models;

namespace WinSereno.Services
{
    public interface ISessionLogger : IDisposable
    {
        void Write(string message);
        void TaskStarted(MaintenanceTask task);
        void TaskFinished(MaintenanceTask task, MaintenanceTaskResult result);
    }

    public sealed class SessionLogger : ISessionLogger
    {
        private readonly object sync = new object();
        private readonly StreamWriter writer;
        public string FilePath { get; }
        public SessionLogger(PortableStorage storage)
        {
            Directory.CreateDirectory(storage.LogsDirectory);
            var name = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            FilePath = Path.Combine(storage.LogsDirectory, name + ".log");
            for (var suffix = 1; File.Exists(FilePath); suffix++)
                FilePath = Path.Combine(storage.LogsDirectory, name + "_" + suffix + ".log");
            writer = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        }
        public void Write(string message)
        {
            lock (sync) writer.WriteLine("[{0:O}] {1}", DateTimeOffset.Now, message);
        }
        public void TaskStarted(MaintenanceTask task)
        {
            Write($"Tarea iniciada | Id={task.Id} | Nombre={task.Name} | Mock={task.IsMock}");
            if (task.Id == ElevatedTaskCatalog.ChkdskId) Write("Volumen de Windows comprobado=" + task.Arguments);
            Write($"Comando={task.Command}\nArgumentos={task.Arguments}\nRequiere elevación={task.RequiresElevation}");
        }
        public void TaskFinished(MaintenanceTask task, MaintenanceTaskResult result)
        {
            Write($"Tarea finalizada | Id={task.Id} | Inicio={result.StartedAt:O} | Fin={result.FinishedAt:O} | Duración={result.Duration}");
            Write($"Estado={result.ExecutionStatus} | Resultado={result.FindingStatus} | ExitCode={result.ExitCode?.ToString() ?? "no disponible"} | Reinicio={result.RequiresRestart}");
            Write("Resumen=" + result.UserSummary + "\nSTDOUT completo:\n" + result.StdOut + "\nSTDERR completo:\n" + result.StdErr);
        }
        public void Dispose() { lock (sync) writer.Dispose(); }
    }
}
