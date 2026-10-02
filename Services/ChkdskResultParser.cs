using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WinSereno.Models;
namespace WinSereno.Services
{
    public static class ChkdskResultParser
    {
        private static readonly string[] Healthy = {
            "Windows has scanned the file system and found no problems.",
            "Windows has checked the file system and found no problems.",
            "Windows comprobo el sistema de archivos y no encontro problemas.",
            "Windows ha examinado el sistema de archivos y no encontro problemas.",
            "Windows ha examinado el sistema de archivos y no encontro ningun problema.",
            "Windows ha comprobado el sistema de archivos y no encontro problemas.",
            "Windows ha comprobado el sistema de archivos y no ha encontrado problemas." };
        private static readonly string[] Attention = {
            "Windows found problems with the file system.",
            "Windows has found problems with the file system.",
            "Windows has scanned the file system and found problems.",
            "Windows has checked the file system and found problems.",
            "Windows comprobo el sistema de archivos y detecto problemas.",
            "Windows ha comprobado el sistema de archivos y detecto problemas.",
            "Windows encontro problemas en el sistema de archivos.",
            "Se encontraron problemas en el sistema de archivos.",
            "Windows ha encontrado problemas en el sistema de archivos.",
            "Errors found. CHKDSK cannot continue in read-only mode.",
            "Se encontraron errores. CHKDSK no puede continuar en modo de solo lectura.",
            "CHKDSK cannot continue in read-only mode.",
            "CHKDSK no puede continuar en modo de solo lectura.",
            "Run CHKDSK with the /F (fix) option to correct these.",
            "Ejecute CHKDSK con la opcion /F (corregir) para corregir estos errores." };
        private static readonly string[] BitmapProblems = {
            "The Volume Bitmap is incorrect.", "El mapa de bits del volumen es incorrecto." };
        private static readonly string[] ReadOnly = {
            "CHKDSK is running in read-only mode.", "Ejecutando CHKDSK en modo de solo lectura." };
        private static readonly string[] ScanRecommendation = {
            "Run chkdsk /scan to find the problems and queue them for repair.",
            "Ejecute chkdsk /scan para encontrar los problemas y ponerlos en cola para su reparacion." };
        private static readonly string[] Failed = {
            "Cannot open volume for direct access.", "No se puede abrir el volumen para acceso directo.",
            "CHKDSK is not available for RAW drives.", "CHKDSK no esta disponible para unidades RAW.",
            "Access Denied as you do not have sufficient privileges", "Acceso denegado porque no tiene privilegios suficientes",
            "Unable to determine volume version and state. CHKDSK aborted.", "No se puede determinar la version y el estado del volumen. CHKDSK se anulo." };
        private static string Normalize(string value)
        {
            var text = new StringBuilder();
            foreach (char c in (value ?? "").Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) text.Append(c);
            return Regex.Replace(text.ToString(), @"\s+", " ").Trim();
        }
        private static bool Contains(string value, string[] phrases)
        {
            return Array.Exists(phrases, phrase => Regex.IsMatch(value,
                @"(?:^|[.!?] )" + Regex.Escape(phrase) + @"(?= |$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }
        public static void Apply(MaintenanceTaskResult result)
        {
            result.RequiresRestart = false;
            if (result.ExecutionStatus == ExecutionStatus.Cancelled)
            { result.FindingStatus = FindingStatus.Unknown; result.UserSummary = "Comprobación cancelada por el usuario."; return; }
            string text = Normalize(result.StdOut), error = Normalize(result.StdErr);
            bool healthy = Contains(text, Healthy), attention = Contains(text, Attention) ||
                (Contains(text, BitmapProblems) && Contains(text, ReadOnly));
            bool failed = Contains(text, Failed) || Contains(error, Failed);
            if (failed)
            { result.ExecutionStatus = ExecutionStatus.Failed; result.FindingStatus = FindingStatus.ScanFailed; }
            else if (result.ExitCode.HasValue)
            {
                int code = result.ExitCode.Value;
                // Code 3 also covers errors left unfixed without /f: content is required to distinguish that from a failed check.
                result.ExecutionStatus = code == 0 || (attention && !healthy && code >= 1 && code <= 3) ? ExecutionStatus.Success :
                    code >= 1 && code <= 3 ? ExecutionStatus.Unknown : ExecutionStatus.Failed;
                result.FindingStatus = healthy && !attention && code == 0 ? FindingStatus.Healthy :
                    attention && !healthy && code >= 0 && code <= 3 ? FindingStatus.Attention : FindingStatus.Unknown;
            }
            else { result.FindingStatus = FindingStatus.Unknown; }
            result.UserSummary = result.FindingStatus == FindingStatus.Healthy ? "CHKDSK no ha encontrado problemas en el sistema de archivos de la unidad comprobada." :
                result.FindingStatus == FindingStatus.Attention ? "CHKDSK informa de inconsistencias que necesitan verificación posterior. En una partición activa pueden ser transitorias; no se ha realizado ninguna reparación." :
                result.ExecutionStatus == ExecutionStatus.Failed ? "No se pudo completar o confirmar la comprobación. Consulta la salida original; esto no demuestra un daño en el disco." :
                "No se pudo interpretar automáticamente el resultado de CHKDSK. Consulta la salida original.";
            if (result.FindingStatus == FindingStatus.Attention && Contains(text, ScanRecommendation))
                result.UserSummary += " La salida de CHKDSK recomienda chkdsk /scan para una comprobación posterior. Es solo información; WinSereno no ha ejecutado ni programado esa acción.";
        }
    }
}
