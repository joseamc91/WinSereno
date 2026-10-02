using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.ServiceProcess;
using WinSereno.Models;

namespace WinSereno.Services
{
    public static class DiagnosticPolicy
    {
        public static readonly KeyValuePair<string, string>[] EssentialServices = {
            new KeyValuePair<string, string>("RpcSs", "Llamada a procedimiento remoto (RPC)"),
            new KeyValuePair<string, string>("DcomLaunch", "Iniciador de procesos de servidor DCOM"),
            new KeyValuePair<string, string>("EventLog", "Registro de eventos de Windows") };
        public const int EventLookbackDays = 7;
        public const int MaximumEventsRead = 100;
        public const int MaximumEventsShown = 20;
    }
    internal sealed class BasicDiagnosticChecks
    {
        private readonly SystemInformationService information;
        private readonly ISessionLogger logger;
        public BasicDiagnosticChecks(SystemInformationService information, ISessionLogger logger) { this.information = information; this.logger = logger; }
        public DiagnosticResult Space()
        {
            var disks = information.ReadDisks();
            bool low = disks.Volumes.Any(d => d.NeedsAttention);
            var result = new DiagnosticResult { Id = "space", Name = "Espacio de almacenamiento",
                Status = low ? DiagnosticStatus.Attention : disks.HasErrors || disks.Volumes.Count == 0 ? DiagnosticStatus.NotChecked : DiagnosticStatus.Healthy,
                Summary = low ? "Alguna unidad tiene poco espacio libre." : disks.HasErrors ? "No se pudieron consultar todas las unidades." : disks.Volumes.Count == 0 ? "No hay volúmenes locales listos." : "Todas las unidades consultadas tienen al menos un " + SystemInformationPolicy.LowDiskSpacePercentage.ToString("0.#", CultureInfo.CurrentCulture) + " % libre.",
                DetailedDescription = string.Join("\n", disks.Volumes.Select(d => d.Unit + " tiene " + d.FreePercentage.ToString("0.#", CultureInfo.CurrentCulture) + " % de espacio libre.")) + (disks.HasErrors ? "\nLa consulta de alguna unidad fue incompleta." : ""),
                Recommendation = low ? "Revisar qué archivos ocupan espacio; no se ha realizado ninguna limpieza." : "Ver los volúmenes en Inicio.", NavigationTarget = NavigationSection.Home, NavigationLabel = "Ir a Inicio" };
            return result;
        }
        public DiagnosticResult StorageHealth()
        {
            var rows = SystemQuery.ReadNamespace(@"\\.\root\Microsoft\Windows\Storage", "SELECT DeviceId, FriendlyName, HealthStatus FROM MSFT_PhysicalDisk", "DeviceId", "FriendlyName", "HealthStatus");
            var details = new List<string>(); bool warning = false, unhealthy = false, unknown = rows.Count == 0;
            foreach (var row in rows)
            {
                int value = row["HealthStatus"] == null ? 5 : Convert.ToInt32(row["HealthStatus"]);
                warning |= value == 1; unhealthy |= value == 2; unknown |= value != 0 && value != 1 && value != 2;
                details.Add("Disco físico " + row["DeviceId"] + " · " + row["FriendlyName"] + ": " + (value == 0 ? "salud básica normal" : value == 1 ? "advertencia del proveedor" : value == 2 ? "salud degradada según el proveedor" : "estado desconocido"));
            }
            return new DiagnosticResult { Id = "storage-health", Name = "Salud básica de almacenamiento",
                Status = unhealthy ? DiagnosticStatus.Error : warning ? DiagnosticStatus.Attention : unknown ? DiagnosticStatus.NotChecked : DiagnosticStatus.Healthy,
                Summary = unhealthy ? "Windows informa de un disco físico con salud degradada." : warning ? "Windows informa de una advertencia de salud." : unknown ? "El proveedor no expone un estado de salud completo y fiable." : "Windows informa de salud básica normal en los discos expuestos.",
                DetailedDescription = string.Join("\n", details) + "\nEs información del proveedor de almacenamiento, no un análisis SMART completo. No se han asociado letras de volumen a discos físicos.",
                Recommendation = unhealthy || warning ? "Conservar una copia de los datos y revisar el dispositivo indicado." : "Esta comprobación no descarta problemas no expuestos por el controlador." };
        }
        public DiagnosticResult Services()
        {
            var lines = new List<string>(); bool stopped = false, unknown = false, transition = false;
            foreach (var entry in DiagnosticPolicy.EssentialServices)
            {
                try
                {
                    using (var service = new ServiceController(entry.Key))
                    {
                        var state = service.Status;
                        lines.Add(entry.Value + " (" + entry.Key + "): " + state);
                        stopped |= state == ServiceControllerStatus.Stopped;
                        transition |= state != ServiceControllerStatus.Stopped && state != ServiceControllerStatus.Running;
                    }
                }
                catch (Exception ex) { unknown = true; lines.Add(entry.Value + ": no se pudo consultar"); SystemQuery.Log(logger, "Error servicio " + entry.Key + ": " + ex); }
            }
            return new DiagnosticResult { Id = "services", Name = "Servicios críticos",
                Status = stopped ? DiagnosticStatus.Error : unknown || transition ? DiagnosticStatus.NotChecked : DiagnosticStatus.Healthy,
                Summary = stopped ? "Un servicio esencial está detenido." : unknown || transition ? "No se pudo confirmar el estado esperado de todos los servicios." : "Los tres servicios esenciales están en ejecución.",
                DetailedDescription = string.Join("\n", lines), Recommendation = stopped ? "Revisar el motivo de la detención; la aplicación no inicia ni modifica servicios." : "Solo se consultan RPC, DCOM y Registro de eventos; no servicios iniciados bajo demanda." };
        }
        public DiagnosticResult Power()
        {
            string name = NativePowerInformation.Read();
            return new DiagnosticResult { Id = "power", Name = "Plan de energía", Status = string.IsNullOrWhiteSpace(name) ? DiagnosticStatus.NotChecked : DiagnosticStatus.Healthy,
                Summary = string.IsNullOrWhiteSpace(name) ? "No se pudo obtener el nombre del plan activo." : "Plan activo: " + name,
                Recommendation = "Información de solo lectura. No se valora ni cambia el plan seleccionado." };
        }
    }
}
