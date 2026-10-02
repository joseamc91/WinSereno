using System;
using System.Text.RegularExpressions;
using WinSereno.Models;
namespace WinSereno.Services
{
    public static class WinsockResetResultParser
    {
        private static readonly string[] Success = {
            "Successfully reset the Winsock Catalog.",
            "El catálogo Winsock se restableció correctamente.",
            "El catálogo de Winsock se restableció correctamente.",
            "Se restableció correctamente el catálogo Winsock."
        };
        private static readonly string[] Restart = {
            "You must restart the computer in order to complete the reset.",
            "Debe reiniciar el equipo para completar el restablecimiento.",
            "Reinicie el equipo para completar el restablecimiento."
        };
        private static readonly string[] Denied = {
            "Access is denied.", "Acceso denegado.", "The requested operation requires elevation (Run as administrator).",
            "The requested operation requires elevation.", "La operación solicitada requiere elevación.",
            "La operación solicitada requiere ejecución con elevación.",
            "La operación solicitada requiere elevación (Ejecutar como administrador)."
        };
        private static bool Has(string text, string[] phrases) => Array.Exists(phrases, phrase =>
            Regex.IsMatch(text ?? "", @"(?:\A|[\r\n])\s*" + Regex.Escape(phrase) + @"\s*(?=[\r\n]|\z)", RegexOptions.IgnoreCase));
        public static void Apply(MaintenanceTaskResult result)
        {
            result.FindingStatus = FindingStatus.Unknown; result.RequiresRestart = false;
            if (result.ExecutionStatus == ExecutionStatus.Cancelled)
            { result.UserSummary = "Elevación cancelada por el usuario; no se restableció Winsock."; return; }
            if (!result.ExitCode.HasValue)
            { result.ExecutionStatus = ExecutionStatus.Failed; result.UserSummary = "No se pudo completar o confirmar el restablecimiento Winsock. Consulta los detalles."; return; }
            bool denied = Has(result.StdOut, Denied) || Has(result.StdErr, Denied);
            result.ExecutionStatus = result.ExitCode == 0 && !denied ? ExecutionStatus.Success : ExecutionStatus.Failed;
            if (result.ExecutionStatus == ExecutionStatus.Failed)
            { result.UserSummary = "Windows no pudo completar el restablecimiento Winsock. Consulta la salida original."; return; }
            result.RequiresRestart = Has(result.StdOut, Restart) || Has(result.StdErr, Restart);
            bool confirmed = Has(result.StdOut, Success) && string.IsNullOrWhiteSpace(result.StdErr);
            if (confirmed) result.FindingStatus = FindingStatus.Completed;
            result.UserSummary = confirmed ? "Windows confirmó el restablecimiento del catálogo Winsock. Esto no confirma que Internet funcione." :
                "El comando terminó con ExitCode 0, pero no se pudo interpretar automáticamente el restablecimiento Winsock. Consulta la salida original.";
            result.UserSummary += result.RequiresRestart ? " Windows indica que debes reiniciar para completar el cambio; no se reinicia automáticamente." :
                " Puede ser necesario reiniciar Windows para completar el cambio; no se reinicia automáticamente.";
        }
    }
}
