using System;
using System.Text.RegularExpressions;
using WinSereno.Models;
namespace WinSereno.Services
{
    public static class FlushDnsResultParser
    {
        private static readonly string[] SuccessMessages = {
            "Successfully flushed the DNS Resolver Cache.",
            "Se vació correctamente la caché de resolución de DNS."
        };
        private static readonly string[] PermissionMessages = {
            "The requested operation requires elevation.",
            "La operación solicitada requiere elevación.",
            "La operación solicitada requiere ejecución con elevación.",
            "Access is denied.", "Acceso denegado."
        };
        private static bool Has(string output, string phrase) => Regex.IsMatch(output ?? "", @"(?:\A|[\r\n])\s*" + Regex.Escape(phrase) + @"\s*(?=[\r\n]|\z)", RegexOptions.IgnoreCase);
        public static bool NeedsElevation(MaintenanceTaskResult result) => result.ExecutionStatus != ExecutionStatus.Cancelled &&
            result.ExitCode.HasValue && result.ExitCode != 0 && (result.ExitCode == 5 ||
                Array.Exists(PermissionMessages, p => Has(result.StdOut, p) || Has(result.StdErr, p)));
        public static void Apply(MaintenanceTaskResult result)
        {
            result.RequiresRestart = false; result.FindingStatus = FindingStatus.Unknown;
            if (result.ExecutionStatus == ExecutionStatus.Cancelled) { result.UserSummary = "Operación cancelada por el usuario."; return; }
            if (!result.ExitCode.HasValue) { result.UserSummary = "No se pudo completar el vaciado de caché DNS. Consulta los detalles."; return; }
            result.ExecutionStatus = result.ExitCode == 0 ? ExecutionStatus.Success : ExecutionStatus.Failed;
            if (result.ExitCode != 0) { result.UserSummary = "Windows no pudo vaciar la caché DNS. Consulta la salida para conocer el motivo."; return; }
            if (Array.Exists(SuccessMessages, p => Has(result.StdOut, p)) && !Array.Exists(PermissionMessages, p => Has(result.StdOut, p) || Has(result.StdErr, p)))
            { result.FindingStatus = FindingStatus.Completed; result.UserSummary = "Windows confirmó el vaciado de la caché DNS. Esto no verifica la conectividad de Internet."; }
            else result.UserSummary = "El comando terminó con ExitCode 0, pero no se pudo confirmar automáticamente el vaciado de la caché DNS. Consulta la salida original.";
        }
    }
}
