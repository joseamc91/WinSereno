using System;
using System.IO;
using WinSereno.Models;
namespace WinSereno.Services
{
    internal static class WindowsTempAnalysisProtocol
    {
        public static void Write(BinaryWriter writer, CleanupCategoryResult value)
        {
            WorkerProtocol.WriteText(writer, value.Path); writer.Write(value.IsAvailable);
            writer.Write(value.TotalBytes); writer.Write(value.FileCount);
            writer.Write(value.PotentiallyCleanableBytes ?? 0); writer.Write(value.PotentiallyCleanableFileCount ?? 0);
            writer.Write(value.AccessDeniedCount); writer.Write(value.LockedCount); writer.Write(value.OtherErrorsCount);
            writer.Write(value.ReparsePointCount); writer.Write(value.Duration.Ticks); WorkerProtocol.WriteText(writer, value.Information);
            writer.Write(value.AnalysisFinishedAt.HasValue);
            if (value.AnalysisFinishedAt.HasValue) writer.Write(value.AnalysisFinishedAt.Value.UtcTicks);
        }
        public static CleanupCategoryResult Read(BinaryReader reader)
        {
            var value = new CleanupCategoryResult { Category = CleanupCategory.WindowsTemporary, Name = "Temporales de Windows", WasAnalyzed = true,
                Path = WorkerProtocol.ReadText(reader), IsAvailable = reader.ReadBoolean(), TotalBytes = Count(reader), FileCount = Count(reader),
                PotentiallyCleanableBytes = Count(reader), PotentiallyCleanableFileCount = Count(reader), AccessDeniedCount = Count(reader),
                LockedCount = Count(reader), OtherErrorsCount = Count(reader), ReparsePointCount = Count(reader), Duration = TimeSpan.FromTicks(Count(reader)),
                Information = WorkerProtocol.ReadText(reader) + " · Análisis administrativo de esta sesión (solo lectura)." };
            if (reader.ReadBoolean()) value.AnalysisFinishedAt = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero).ToLocalTime();
            if (!string.Equals(value.Path, CleanupAnalysisService.WindowsTemporaryPath, StringComparison.OrdinalIgnoreCase) ||
                value.PotentiallyCleanableBytes > value.TotalBytes || value.PotentiallyCleanableFileCount > value.FileCount)
                throw new InvalidDataException("Resultado de análisis administrativo inválido.");
            return value;
        }
        private static long Count(BinaryReader reader)
        { long value = reader.ReadInt64(); if (value < 0) throw new InvalidDataException("Contador negativo."); return value; }
        public static void Apply(MaintenanceTaskResult result)
        {
            var value = result.WindowsTempAnalysis;
            result.ExitCode = null;
            if (value == null)
            {
                result.FindingStatus = FindingStatus.Unknown;
                result.UserSummary = result.ExecutionStatus == ExecutionStatus.Cancelled ? "Elevación cancelada por el usuario; no se realizó el análisis." : "No se pudo completar el análisis administrativo de temporales de Windows.";
                return;
            }
            result.ExecutionStatus = value.IsAvailable ? ExecutionStatus.Success : ExecutionStatus.Failed;
            result.FindingStatus = !value.IsAvailable ? FindingStatus.Unknown : value.IsPartial ? FindingStatus.PartiallyCompleted : FindingStatus.Completed;
            result.UserSummary = "Análisis administrativo " + (!value.IsAvailable ? "no disponible" : value.IsPartial ? "parcial" : "completado") +
                " | Ruta=" + value.Path + " | TotalBytes=" + value.TotalBytes + " | Archivos=" + value.FileCount +
                " | PotencialBytes=" + value.PotentiallyCleanableBytes + " | PotencialArchivos=" + value.PotentiallyCleanableFileCount +
                " | AccessDenied=" + value.AccessDeniedCount + " | Bloqueados=" + value.LockedCount + " | Otros errores=" + value.OtherErrorsCount +
                " | Reparse omitidos=" + value.ReparsePointCount + " | Duración análisis=" + value.Duration + " | " + value.Information;
        }
    }
}
