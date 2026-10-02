using System;
using System.Text.RegularExpressions;
using WinSereno.Models;
namespace WinSereno.Services
{
    public static class TcpIpResetResultParser
    {
        private static readonly Regex Step = new Regex(
            @"^(?:Resetting|Restableciendo|Restablecimiento de)\s*(?<item>.*?)[,:]\s*(?<state>OK!|correcto[.!]?|correctamente[.!]?|failed[.!]?|error[.!]?|err[oó]neo[.!]?)$", RegexOptions.IgnoreCase);
        private static readonly Regex SpanishStep = new Regex(
            @"^(?:Restableciendo|Restablecimiento de)\s+(?<item>.+?)\s+(?<state>correcto[.!]?|correctamente[.!]?|error[.!]?|err[oó]neo[.!]?)$", RegexOptions.IgnoreCase);
        private static readonly string[] RestartMessages = {
            "Restart the computer to complete this action.", "You must restart the computer in order to complete the reset.",
            "Reinicie el equipo para completar esta acción.", "Reinicie el equipo para completar el restablecimiento.",
            "Debe reiniciar el equipo para completar el restablecimiento."
        };
        private static readonly string[] FailureMessages = {
            "Access is denied.", "Acceso denegado.", "The requested operation requires elevation (Run as administrator).",
            "The requested operation requires elevation.", "La operación solicitada requiere elevación.",
            "La operación solicitada requiere ejecución con elevación.",
            "La operación solicitada requiere elevación (Ejecutar como administrador)."
        };
        private static bool Is(string line, string[] messages) => Array.Exists(messages, value => string.Equals(line, value, StringComparison.OrdinalIgnoreCase));
        public static void Apply(MaintenanceTaskResult result)
        {
            result.FindingStatus = FindingStatus.Unknown; result.RequiresRestart = false;
            if (result.ExecutionStatus == ExecutionStatus.Cancelled)
            { result.UserSummary = "Elevación cancelada por el usuario; no se restableció TCP/IP."; return; }
            int successful = 0, failed = 0, unrecognized = 0;
            foreach (string raw in ((result.StdOut ?? "") + "\n" + (result.StdErr ?? "")).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim(); if (line.Length == 0) continue;
                if (Is(line, RestartMessages)) { result.RequiresRestart = true; continue; }
                if (Is(line, FailureMessages)) { failed++; continue; }
                var match = Step.Match(line);
                if (!match.Success) match = SpanishStep.Match(line);
                if (!match.Success) { unrecognized++; continue; }
                string state = match.Groups["state"].Value;
                if (state.StartsWith("OK", StringComparison.OrdinalIgnoreCase) || state.StartsWith("correct", StringComparison.OrdinalIgnoreCase)) successful++; else failed++;
            }
            bool executionFailed = !result.ExitCode.HasValue || result.ExitCode != 0;
            result.ExecutionStatus = executionFailed || failed > 0 ? ExecutionStatus.Failed : ExecutionStatus.Success;
            if (successful > 0 && (failed > 0 || executionFailed))
            {
                result.FindingStatus = FindingStatus.PartiallyCompleted;
                result.UserSummary = "Restablecimiento TCP/IP parcial: Windows confirmó " + successful + " pasos, pero también informó de fallos o no se confirmó la ejecución completa. La configuración puede haber cambiado parcialmente. Consulta stdout/stderr y ExitCode; no se ejecutará ninguna herramienta adicional.";
            }
            else if (executionFailed || failed > 0)
                result.UserSummary = "Windows no pudo completar el restablecimiento TCP/IP. Consulta la salida original. No se puede garantizar que no haya aplicado cambios.";
            else if (successful > 0 && unrecognized == 0)
            {
                result.FindingStatus = FindingStatus.Completed;
                result.UserSummary = "Los pasos de restablecimiento TCP/IP informados por Windows terminaron correctamente. Esto no verifica ni garantiza la conectividad.";
            }
            else result.UserSummary = "El comando terminó con ExitCode 0, pero su salida no permite confirmar automáticamente el restablecimiento TCP/IP. Consulta los detalles.";
            if (result.RequiresRestart)
                result.UserSummary += " Windows indica que debes reiniciar para completar el cambio; la aplicación nunca reinicia automáticamente.";
            else if (successful > 0 || result.ExitCode == 0)
                result.UserSummary += " Normalmente se recomienda reiniciar Windows para completar el restablecimiento; la salida no confirmó un requisito explícito. Nunca se reinicia automáticamente.";
        }
    }
}
