using System;
using System.Text.RegularExpressions;
using WinSereno.Models;
namespace WinSereno.Services
{
    public static class TcpIpResetResultParser
    {
        private static readonly Regex Step = new Regex(
            @"^(?:Resetting|Restableciendo|Restablecimiento de)\s*(?<item>.*?)[,:]\s*(?<state>OK|correcto|correctamente|failed|error|err[oó]neo)[.!?:;,]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex SpanishStep = new Regex(
            @"^(?:Restableciendo|Restablecimiento de)\s+(?<item>.+?)\s+(?<state>correcto|correctamente|error|err[oó]neo)[.!?:;,]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex SpanishCompleted = new Regex(
            @"^(?!.*\bno\s+se\s+restableci[oó]\b)(?<item>[\p{L}\p{N}][\p{L}\p{N}\s/()._-]*?)\s+se\s+restableci[oó]\s+correctamente[.!?:;,]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex SpanishFailed = new Regex(
            @"^Error al restablecer(?:\s+.*?)?[.!?:;,]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
        private static readonly char[] ClosingPunctuation = { '.', '!', '?', ':', ';', ',' };
        private static bool Is(string line, string[] messages) => Array.Exists(messages, value => string.Equals(line.TrimEnd(ClosingPunctuation), value.TrimEnd(ClosingPunctuation), StringComparison.OrdinalIgnoreCase));
        public static void Apply(MaintenanceTaskResult result)
        {
            result.FindingStatus = FindingStatus.Unknown; result.RequiresRestart = false;
            if (result.ExecutionStatus == ExecutionStatus.Cancelled)
            { result.UserSummary = "Elevación cancelada por el usuario; no se restableció TCP/IP."; return; }
            int successful = 0, failed = 0, unrecognized = 0;
            foreach (string raw in ((result.StdOut ?? "") + "\n" + (result.StdErr ?? "")).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = Regex.Replace(raw.Trim(), @"\s+", " "); if (line.Length == 0) continue;
                if (Is(line, RestartMessages)) { result.RequiresRestart = true; continue; }
                if (Is(line, FailureMessages) || SpanishFailed.IsMatch(line)) { failed++; continue; }
                if (SpanishCompleted.IsMatch(line)) { successful++; continue; }
                var match = Step.Match(line);
                if (!match.Success) match = SpanishStep.Match(line);
                if (!match.Success) { unrecognized++; continue; }
                string state = match.Groups["state"].Value;
                if (state.StartsWith("OK", StringComparison.OrdinalIgnoreCase) || state.StartsWith("correct", StringComparison.OrdinalIgnoreCase)) successful++; else failed++;
            }
            result.ExecutionStatus = ExecutionStatus.Failed;
            if (!result.ExitCode.HasValue)
                result.UserSummary = "No se obtuvo un resultado final del comando TCP/IP. Consulta la salida original; no se puede garantizar que no haya aplicado cambios.";
            else if (successful > 0 && (failed > 0 || result.ExitCode != 0 || unrecognized > 0))
            {
                result.ExecutionStatus = ExecutionStatus.Success;
                result.FindingStatus = FindingStatus.PartiallyCompleted;
                result.UserSummary = failed > 0 || result.ExitCode != 0
                    ? "Windows restableció parcialmente TCP/IP, pero una o más entradas no pudieron modificarse."
                    : "Windows confirmó cambios en TCP/IP, pero no se pudo interpretar toda la salida ni confirmar el restablecimiento completo.";
                result.UserSummary += " Se confirmaron " + successful + " pasos. Consulta la salida original y ExitCode para ver los errores o datos no interpretados. Esto no garantiza la conectividad.";
            }
            else if (result.ExitCode != 0 || failed > 0)
                result.UserSummary = "Windows no pudo completar el restablecimiento TCP/IP. Consulta la salida original. No se puede garantizar que no haya aplicado cambios.";
            else if (successful > 0 && unrecognized == 0)
            {
                result.ExecutionStatus = ExecutionStatus.Success;
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
