using System;
using System.Text.RegularExpressions;
using WinSereno.Models;

namespace WinSereno.Services
{
    public static class SfcVerifyOnlyResultParser
    {
        private static readonly string[][] Phrases = {
            new[] { "Windows Resource Protection did not find any integrity violations.", "Protección de recursos de Windows no encontró ninguna infracción de integridad." },
            new[] { "Windows Resource Protection found integrity violations.", "Protección de recursos de Windows encontró infracciones de integridad." },
            new[] { "Windows Resource Protection could not perform the requested operation.", "Protección de recursos de Windows no pudo realizar la operación solicitada." }
        };
        public static FindingStatus Parse(string output)
        {
            string text = Regex.Replace(output ?? "", @"\s+", " ").Trim();
            int found = -1;
            for (int i = 0; i < Phrases.Length; i++)
                if (Array.Exists(Phrases[i], p => Regex.IsMatch(text, @"(?:^|\. )" + Regex.Escape(p) + @"(?= |$)", RegexOptions.IgnoreCase)))
                { if (found >= 0) return FindingStatus.Unknown; found = i; }
            return found == 0 ? FindingStatus.Healthy : found == 1 ? FindingStatus.Attention : found == 2 ? FindingStatus.ScanFailed : FindingStatus.Unknown;
        }
        public static void Apply(MaintenanceTaskResult result)
        {
            if (result.ExecutionStatus == ExecutionStatus.Cancelled)
            { result.FindingStatus = FindingStatus.Unknown; result.UserSummary = "Verificación no iniciada: permisos cancelados."; return; }
            result.FindingStatus = result.CommandStarted && result.ExitCode.HasValue ? Parse(result.StdOut) : FindingStatus.Unknown;
            // SFC can report detected violations with a nonzero code. Only this explicit outcome and code 1 are accepted.
            bool confirmed = result.ExitCode == 0 || (result.ExitCode == 1 && result.FindingStatus == FindingStatus.Attention);
            result.ExecutionStatus = confirmed && result.FindingStatus != FindingStatus.ScanFailed ? ExecutionStatus.Success : ExecutionStatus.Failed;
            if (!confirmed && result.FindingStatus != FindingStatus.ScanFailed) result.FindingStatus = FindingStatus.Unknown;
            result.RequiresRestart = false;
            result.UserSummary = result.FindingStatus == FindingStatus.Healthy ? "Windows no encontró infracciones de integridad en archivos protegidos." :
                result.FindingStatus == FindingStatus.Attention ? "Windows detectó infracciones de integridad en archivos protegidos. No se realizó ninguna reparación." :
                result.FindingStatus == FindingStatus.ScanFailed ? "Protección de recursos de Windows no pudo realizar la operación solicitada. Esto no demuestra corrupción." :
                "No se pudo confirmar la verificación de los archivos protegidos. Consulta la salida original.";
        }
    }
}
