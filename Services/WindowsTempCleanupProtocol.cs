using System;
using System.IO;
using WinSereno.Models;
namespace WinSereno.Services
{
    internal static class WindowsTempCleanupProtocol
    {
        public static void Write(BinaryWriter writer, MaintenanceTaskResult result)
        {
            var value = result.WindowsTempCleanup;
            writer.Write((byte)value.Status); writer.Write(value.DeletedFiles); writer.Write(value.DeletedDirectories);
            writer.Write(value.RecoveredBytes); writer.Write(value.ProtectedElements); writer.Write(value.InaccessibleElements);
            writer.Write(value.ReparsePoints); writer.Write(value.Duration.Ticks); WorkerProtocol.WriteText(writer, value.TechnicalDetails);
            writer.Write(result.StartedAt.UtcTicks); writer.Write(result.FinishedAt.UtcTicks); writer.Write(result.Duration.Ticks);
            WindowsTempAnalysisProtocol.Write(writer, result.WindowsTempAnalysis);
        }
        public static void ReadInto(BinaryReader reader, MaintenanceTaskResult result)
        {
            var value = new UserTempCleanupResult { Status = (UserTempCleanupStatus)reader.ReadByte(), DeletedFiles = Count(reader),
                DeletedDirectories = Count(reader), RecoveredBytes = Count(reader), ProtectedElements = Count(reader),
                InaccessibleElements = Count(reader), ReparsePoints = Count(reader), Duration = TimeSpan.FromTicks(Count(reader)), TechnicalDetails = WorkerProtocol.ReadText(reader) };
            if (!Enum.IsDefined(typeof(UserTempCleanupStatus), value.Status)) throw new InvalidDataException("Estado de limpieza inválido.");
            var started = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero).ToLocalTime();
            var finished = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero).ToLocalTime();
            var duration = TimeSpan.FromTicks(Count(reader));
            if (finished < started) throw new InvalidDataException("Fechas de limpieza inválidas.");
            var analysis = WindowsTempAnalysisProtocol.Read(reader);
            result.WindowsTempCleanup = value; result.WindowsTempAnalysis = analysis;
            result.StartedAt = started; result.FinishedAt = finished; result.Duration = duration;
        }
        private static long Count(BinaryReader reader)
        { long value = reader.ReadInt64(); if (value < 0) throw new InvalidDataException("Contador negativo."); return value; }
        public static string AnalysisSummary(CleanupCategoryResult value)
            => value == null ? "Reanálisis no disponible" : "Ruta=" + value.Path + " | TotalBytes=" + value.TotalBytes +
                " | Archivos=" + value.FileCount + " | PotencialBytes=" + value.PotentiallyCleanableBytes + " | PotencialArchivos=" + value.PotentiallyCleanableFileCount +
                " | Disponible=" + value.IsAvailable + " | Parcial=" + value.IsPartial + " | AccessDenied=" + value.AccessDeniedCount +
                " | Bloqueados=" + value.LockedCount + " | Otros errores=" + value.OtherErrorsCount + " | Reparse=" + value.ReparsePointCount + " | Duración=" + value.Duration;
        public static void Apply(MaintenanceTaskResult result)
        {
            result.ExitCode = null;
            var value = result.WindowsTempCleanup;
            if (value == null)
            {
                result.FindingStatus = FindingStatus.Unknown;
                result.UserSummary = result.ExecutionStatus == ExecutionStatus.Cancelled ? "Elevación cancelada; no se limpiaron temporales de Windows." :
                    "No se pudo confirmar el resultado de la limpieza de temporales de Windows. Consulta el log.";
                return;
            }
            result.StdErr = value.TechnicalDetails;
            result.ExecutionStatus = value.Status == UserTempCleanupStatus.Failed ? ExecutionStatus.Failed : ExecutionStatus.Success;
            result.FindingStatus = value.Status == UserTempCleanupStatus.Failed ? FindingStatus.Unknown :
                value.Status == UserTempCleanupStatus.Partial ? FindingStatus.PartiallyCompleted : FindingStatus.Completed;
            result.UserSummary = "Temporales de Windows · " + value.Summary + " · Duración limpieza: " + value.Duration.TotalSeconds.ToString("0.00") + " s" +
                " · Reanálisis: " + (result.WindowsTempAnalysis == null || !result.WindowsTempAnalysis.IsAvailable ? "no disponible" :
                    result.WindowsTempAnalysis.IsPartial ? "parcial" : "completado") + " · No se ha reiniciado Windows.";
        }
    }
}
