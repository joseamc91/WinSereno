using System;
using WinSereno.Models;
namespace WinSereno.Services
{
    public static class DismCheckHealthResultParser
    {
        private static readonly string[] Healthy = { "No component store corruption detected." };
        private static readonly string[] Repairable = { "The component store is repairable." };
        private static readonly string[] Unrepairable = { "The component store cannot be repaired.", "The component store is not repairable." };
        public static FindingStatus Parse(int exitCode, string stdout)
        {
            if (exitCode != 0) return FindingStatus.Unknown;
            bool healthy = false, repairable = false, unrepairable = false;
            foreach (string raw in (stdout ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                healthy |= Matches(line, Healthy); repairable |= Matches(line, Repairable); unrepairable |= Matches(line, Unrepairable);
            }
            if ((healthy ? 1 : 0) + (repairable ? 1 : 0) + (unrepairable ? 1 : 0) != 1) return FindingStatus.Unknown;
            return healthy ? FindingStatus.Healthy : repairable ? FindingStatus.RepairRequired : FindingStatus.Unrepairable;
        }
        private static bool Matches(string line, string[] phrases) => Array.Exists(phrases, p => string.Equals(line, p, StringComparison.OrdinalIgnoreCase));
        public static string Summary(MaintenanceTaskResult result)
        {
            if (result.ExecutionStatus == ExecutionStatus.Cancelled) return "Operación cancelada por el usuario.";
            if (result.ExecutionStatus != ExecutionStatus.Success) return "No se pudo completar la comprobación. Consulta la salida para conocer el contexto.";
            switch (result.FindingStatus)
            {
                case FindingStatus.Healthy: return "No se ha detectado corrupción conocida en el almacén de componentes.";
                case FindingStatus.RepairRequired: return "Windows ha detectado corrupción reparable en el almacén de componentes. Se recomienda DISM RestoreHealth; no se ejecutará automáticamente.";
                case FindingStatus.Unrepairable: return "Windows informa de corrupción que no puede repararse mediante DISM de forma normal.";
                default: return "No se pudo interpretar automáticamente el resultado.";
            }
        }
    }
}
