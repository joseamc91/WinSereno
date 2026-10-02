using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using WinSereno.Models;

namespace WinSereno.Services
{
    public static class DismScanHealthResultParser
    {
        public static FindingStatus Parse(int code, string output) => DismCheckHealthResultParser.Parse(code == 3010 ? 0 : code,
            Regex.Replace(output ?? "", @"(?m)^\s*\[[= ]*\d{1,3}(?:\.\d+)?%[= ]*\]\s*", ""));
    }
    public static class DismRestoreHealthResultParser
    {
        public static FindingStatus Parse(int code, string output)
        {
            string text = output ?? "";
            if (code == unchecked((int)0x800f081f) || Regex.IsMatch(text, @"(?im)^\s*(?:Error:\s*)?0x800f081f\s*$") ||
                Has(text, "The source files could not be found.")) return FindingStatus.SourceFilesNotFound;
            if (Has(text, "The restore operation failed.")) return Has(text, "The restore operation completed successfully.") ? FindingStatus.Unknown : FindingStatus.RepairFailed;
            if (code != 0 && code != 3010) return FindingStatus.Unknown;
            bool repaired = Has(text, "The restore operation completed successfully.");
            bool healthy = Has(text, "No component store corruption detected.");
            if (repaired && healthy) return FindingStatus.Healthy;
            if (repaired) return FindingStatus.Repaired;
            if (healthy) return FindingStatus.Healthy;
            return FindingStatus.Unknown;
        }
        internal static bool Has(string text, string phrase) => Regex.IsMatch(text ?? "", @"(?:\A|[\r\n])\s*(?:\[[= ]*\d{1,3}(?:\.\d+)?%[= ]*\]\s*)?" + Regex.Escape(phrase) + @"\s*(?=[\r\n]|\z)", RegexOptions.IgnoreCase);
    }
    public static class SfcResultParser
    {
        // Normalize wrapping, not language. Unknown translations remain uninterpreted.
        private static readonly string[][] Phrases = {
            new[] { "Windows Resource Protection did not find any integrity violations.", "Protección de recursos de Windows no encontró ninguna infracción de integridad." },
            new[] { "Windows Resource Protection found corrupt files and successfully repaired them.", "Protección de recursos de Windows encontró archivos dañados y los reparó correctamente." },
            new[] { "Windows Resource Protection found corrupt files but was unable to fix some of them.", "Protección de recursos de Windows encontró archivos dañados pero no pudo corregir algunos de ellos.", "Protección de recursos de Windows encontró archivos dañados, pero no pudo reparar algunos de ellos.", "Protección de recursos de Windows encontró archivos dañados pero no pudo reparar algunos de ellos." },
            new[] { "Windows Resource Protection could not perform the requested operation.", "Protección de recursos de Windows no pudo realizar la operación solicitada." }
        };
        public static FindingStatus Parse(string output)
        {
            string text = Regex.Replace(output ?? "", @"\s+", " ");
            int found = -1;
            for (int i = 0; i < Phrases.Length; i++)
                if (Array.Exists(Phrases[i], p => Regex.IsMatch(text, @"(?:^|\. )" + Regex.Escape(p) + @"(?= |$)", RegexOptions.IgnoreCase)))
                { if (found >= 0) return FindingStatus.Unknown; found = i; }
            return found == 0 ? FindingStatus.Healthy : found == 1 ? FindingStatus.Repaired : found == 2 ? FindingStatus.RepairFailed : found == 3 ? FindingStatus.ScanFailed : FindingStatus.Unknown;
        }
    }
    public static class RepairResultInterpreter
    {
        public static void Apply(string id, MaintenanceTaskResult result)
        {
            if (id == ElevatedTaskCatalog.SfcVerifyOnlyId) { SfcVerifyOnlyResultParser.Apply(result); return; }
            if (id == ElevatedTaskCatalog.ComponentCleanupId) { DismComponentCleanupResultParser.Apply(result); return; }
            if (id == ElevatedTaskCatalog.ChkdskId) { ChkdskResultParser.Apply(result); return; }
            if (id == ElevatedTaskCatalog.ResetTcpIpId) { TcpIpResetResultParser.Apply(result); return; }
            if (id == ElevatedTaskCatalog.ResetWinsockId) { WinsockResetResultParser.Apply(result); return; }
            if (id == ElevatedTaskCatalog.FlushDnsId) { FlushDnsResultParser.Apply(result); return; }
            if (result.ExitCode.HasValue)
            {
                int code = result.ExitCode.Value;
                result.RequiresRestart = id != ElevatedTaskCatalog.SfcId && code == 3010;
                result.ExecutionStatus = code == 0 || result.RequiresRestart ? ExecutionStatus.Success : ExecutionStatus.Failed;
                result.FindingStatus = id == ElevatedTaskCatalog.CheckHealthId ? DismCheckHealthResultParser.Parse(code, result.StdOut) :
                    id == ElevatedTaskCatalog.ScanHealthId ? DismScanHealthResultParser.Parse(code, result.StdOut) :
                    id == ElevatedTaskCatalog.RestoreHealthId ? DismRestoreHealthResultParser.Parse(code, result.StdOut) : SfcResultParser.Parse(result.StdOut);
                if (result.FindingStatus == FindingStatus.ScanFailed) result.ExecutionStatus = ExecutionStatus.Failed;
                if (id == ElevatedTaskCatalog.SfcId && code != 0 && result.FindingStatus != FindingStatus.RepairFailed && result.FindingStatus != FindingStatus.ScanFailed)
                    result.FindingStatus = FindingStatus.Unknown;
            }
            result.UserSummary = Summary(id, result);
            if (result.RequiresRestart) result.UserSummary += " Es necesario reiniciar Windows; la aplicación no lo reinicia.";
        }
        private static string Summary(string id, MaintenanceTaskResult r)
        {
            if (r.ExecutionStatus == ExecutionStatus.Cancelled) return "Operación cancelada por el usuario.";
            switch (r.FindingStatus)
            {
                case FindingStatus.Healthy: return id == ElevatedTaskCatalog.SfcId ? "Windows no ha encontrado archivos protegidos dañados." : "No se ha detectado corrupción en el almacén de componentes.";
                case FindingStatus.Repaired: return id == ElevatedTaskCatalog.SfcId ? "Windows encontró archivos dañados y pudo repararlos." : "La operación de reparación de la imagen de Windows se completó correctamente.";
                case FindingStatus.RepairRequired: return "Se ha detectado corrupción reparable en el almacén de componentes. Se recomienda DISM RestoreHealth.";
                case FindingStatus.Unrepairable: return "Windows informa de corrupción que no puede repararse mediante DISM de forma normal.";
                case FindingStatus.SourceFilesNotFound: return "DISM no pudo obtener los archivos necesarios para completar la reparación.";
                case FindingStatus.RepairFailed: return id == ElevatedTaskCatalog.SfcId ? "Windows encontró archivos dañados que no pudo reparar completamente. Windows guarda información adicional en " + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS", "CBS.log") + ". Pueden necesitarse acciones adicionales." : "DISM no pudo completar la operación de reparación. Consulta la salida original.";
                case FindingStatus.ScanFailed: return "Protección de recursos de Windows no pudo realizar la operación solicitada. Esto no demuestra corrupción.";
                default: return r.ExecutionStatus == ExecutionStatus.Failed ? "No se pudo completar la operación. Consulta la salida para conocer el contexto." : "No se pudo interpretar automáticamente el resultado.";
            }
        }
    }
    public static class ToolProgressParser
    {
        public static double? Parse(string id, string text)
        {
            if (id == ElevatedTaskCatalog.ChkdskId || id == ElevatedTaskCatalog.CheckHealthId || id == ElevatedTaskCatalog.FlushDnsId || id == ElevatedTaskCatalog.ResetWinsockId || id == ElevatedTaskCatalog.ResetTcpIpId) return null;
            string pattern = id == ElevatedTaskCatalog.SfcId ? @"^(?:Verification\s+(?<n>\d{1,3})%\s+complete\.|Se completó (?:un )?(?<n>\d{1,3})%\s+(?:de la verificación|de verificación)\.|Se completó (?:la verificación|la comprobación) de (?<n>\d{1,3})%\.)$" : @"^\s*\[[= ]*(?<n>\d{1,3}(?:\.\d+)?)%[= ]*\]";
            var match = Regex.Match((text ?? "").Trim(), pattern, RegexOptions.IgnoreCase);
            return match.Success && double.TryParse(match.Groups["n"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value) && value <= 100 ? (double?)value : null;
        }
    }
}
