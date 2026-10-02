using System;
using System.Text.RegularExpressions;
using WinSereno.Models;
namespace WinSereno.Services
{
    public static class DismComponentCleanupResultParser
    {
        private static readonly string[] Completed = { "The operation completed successfully.", "La operación se completó correctamente.", "La operación finalizó correctamente." };
        private static readonly string[] Failure = { "The operation failed.", "La operación no se pudo completar.", "La operación falló.", "La operación no se ha completado." };
        private static readonly string[] Restart = { "A restart is required to complete the operation.", "Restart Windows to complete this operation.", "A reboot is required to complete the operation.", "Es necesario reiniciar para completar la operación.", "Es necesario reiniciar Windows para completar esta operación.", "Se requiere reiniciar para completar la operación." };
        private static bool Has(string value, string[] phrases)
        {
            return Array.Exists(phrases, phrase => Regex.IsMatch(value ?? "", @"(?:\A|[\r\n])\s*(?:\[[= ]*\d{1,3}(?:\.\d+)?%[= ]*\]\s*)?" + Regex.Escape(phrase) + @"\s*(?=[\r\n]|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }
        public static void Apply(MaintenanceTaskResult result)
        {
            if (result.ExecutionStatus == ExecutionStatus.Cancelled)
            { result.FindingStatus = FindingStatus.Unknown; result.RequiresRestart = false; result.UserSummary = "Operación cancelada por el usuario; no se inició la limpieza de componentes."; return; }
            bool completed = Has(result.StdOut, Completed);
            bool failed = Has(result.StdOut, Failure) || Has(result.StdErr, Failure) ||
                Regex.IsMatch((result.StdOut ?? "") + "\n" + (result.StdErr ?? ""), @"(?im)^\s*Error:\s*(?:0x[0-9a-f]+|[1-9][0-9]*)\s*$");
            result.RequiresRestart = result.ExitCode == 3010 || Has(result.StdOut, Restart) || Has(result.StdErr, Restart);
            if (result.ExitCode.HasValue)
            {
                bool accepted = result.ExitCode == 0 || result.ExitCode == 3010;
                result.ExecutionStatus = accepted && !failed ? ExecutionStatus.Success : ExecutionStatus.Failed;
                result.FindingStatus = !accepted ? FindingStatus.Failed : failed ? completed ? FindingStatus.Unknown : FindingStatus.Failed :
                    completed ? result.RequiresRestart ? FindingStatus.RestartRequired : FindingStatus.Completed : FindingStatus.Unknown;
            }
            else result.FindingStatus = result.ExecutionStatus == ExecutionStatus.Failed ? FindingStatus.Failed : FindingStatus.Unknown;
            result.UserSummary = result.FindingStatus == FindingStatus.Completed || result.FindingStatus == FindingStatus.RestartRequired ? "DISM confirmó que la limpieza del almacén de componentes terminó correctamente." :
                result.ExecutionStatus == ExecutionStatus.Failed ? "No se pudo completar la limpieza del almacén de componentes. Consulta la salida original." :
                "No se pudo interpretar automáticamente el resultado de la limpieza de componentes. Consulta la salida original.";
            if (result.RequiresRestart) result.UserSummary += " Es necesario reiniciar Windows para completar la operación; la aplicación no reinicia automáticamente.";
        }
    }
}
