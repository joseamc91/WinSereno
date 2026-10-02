using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Threading;
using WinSereno.Models;

namespace WinSereno.Services
{
    internal sealed class EventRule
    {
        public string Provider { get; set; }
        public int Id { get; set; }
        public string Interpretation { get; set; }
    }
    internal sealed class EventDiagnosticCheck
    {
        private readonly ISessionLogger logger;
        public EventDiagnosticCheck(ISessionLogger logger) { this.logger = logger; }
        private static readonly EventRule[] Rules = {
            Rule("Microsoft-Windows-Kernel-Power", 41, "Windows registró un arranque tras un cierre no limpio. Este evento no identifica por sí solo la causa."),
            Rule("EventLog", 6008, "Se registró un apagado inesperado. Puede corresponder al mismo incidente que Kernel-Power 41."),
            Rule("disk", 7, "El proveedor registró un bloque defectuoso. Revisar el dispositivo; este evento no prueba su estado actual."),
            Rule("disk", 51, "Se registró un error de entrada/salida durante una operación de paginación; no identifica por sí solo un fallo permanente."),
            Rule("disk", 153, "Una operación de entrada/salida se reintentó. Puede ser un problema transitorio del almacenamiento."),
            Rule("disk", 157, "Windows registró una retirada inesperada de disco; una desconexión física también puede explicarlo."),
            Rule("Ntfs", 55, "NTFS registró un problema de estructura del sistema de archivos. Revisar contexto y copias de seguridad."),
            Rule("Microsoft-Windows-Ntfs", 55, "NTFS registró un problema de estructura del sistema de archivos. Revisar contexto y copias de seguridad."),
            Rule("Microsoft-Windows-Ntfs", 98, "NTFS registró que un volumen requiere revisión. No se ejecuta CHKDSK."),
            Rule("storahci", 129, "Se registró un reinicio de la ruta/controlador de almacenamiento; puede ser transitorio."),
            Rule("storport", 129, "Se registró un reinicio de la ruta/controlador de almacenamiento; puede ser transitorio."),
            Rule("Microsoft-Windows-WHEA-Logger", 17, "WHEA registró un error de hardware corregido. No equivale a un fallo permanente."),
            Rule("Microsoft-Windows-WHEA-Logger", 19, "WHEA registró un error de hardware corregido. Revisar si se repite."),
            Rule("Microsoft-Windows-WHEA-Logger", 18, "WHEA registró un error de hardware no corregido/grave. Requiere revisar el contexto; no se atribuye a un componente automáticamente.") };
        private static EventRule Rule(string provider, int id, string interpretation) => new EventRule { Provider = provider, Id = id, Interpretation = interpretation };
        public DiagnosticResult Run(CancellationToken token)
        {
            string filter = string.Join(" or ", Rules.Select(r => "(Provider[@Name='" + r.Provider + "'] and EventID=" + r.Id + ")"));
            string xpath = "*[System[TimeCreated[timediff(@SystemTime) <= " + TimeSpan.FromDays(DiagnosticPolicy.EventLookbackDays).TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "] and (Level=1 or Level=2 or Level=3) and (" + filter + ")]]";
            var query = new EventLogQuery("System", PathType.LogName, xpath) { ReverseDirection = true, TolerateQueryErrors = false };
            var events = new List<DiagnosticEvent>(); int processed = 0; bool limited = false;
            var watch = Stopwatch.StartNew();
            using (var reader = new EventLogReader(query))
            {
                while (processed < DiagnosticPolicy.MaximumEventsRead)
                {
                    token.ThrowIfCancellationRequested();
                    if (watch.Elapsed.TotalSeconds >= 4) { limited = true; break; }
                    using (var record = reader.ReadEvent(TimeSpan.FromSeconds(1)))
                    {
                        if (record == null) break;
                        processed++;
                        var rule = Rules.FirstOrDefault(r => string.Equals(r.Provider, record.ProviderName, StringComparison.OrdinalIgnoreCase) && r.Id == record.Id);
                        if (rule == null || events.Count >= DiagnosticPolicy.MaximumEventsShown) continue;
                        string description;
                        try { description = record.FormatDescription(); }
                        catch (Exception ex) { description = "Descripción original no disponible."; SystemQuery.Log(logger, "Descripción de evento no disponible: " + ex.GetType().Name); }
                        if (description != null && description.Length > 8000) description = description.Substring(0, 8000) + "\n[Descripción truncada]";
                        events.Add(new DiagnosticEvent { Time = record.TimeCreated.HasValue ? (DateTimeOffset?)new DateTimeOffset(record.TimeCreated.Value) : null,
                            Provider = record.ProviderName, EventId = record.Id, Level = LevelName(record.Level), Interpretation = rule.Interpretation,
                            WindowsDescription = description ?? "Descripción original no disponible." });
                    }
                }
                limited |= processed >= DiagnosticPolicy.MaximumEventsRead;
            }
            SystemQuery.Log(logger, "Eventos System últimos 7 días: relevantes procesados=" + processed + " | mostrados=" + events.Count + " | límite=" + limited + " | " +
                string.Join(", ", events.GroupBy(e => e.Provider + "/" + e.EventId).Select(g => g.Key + "=" + g.Count())));
            return new DiagnosticResult { Id = "events", Name = "Eventos de Windows", Events = events,
                Status = processed > 0 ? DiagnosticStatus.Attention : limited ? DiagnosticStatus.NotChecked : DiagnosticStatus.Healthy,
                Summary = processed > 0 ? "Se encontraron " + (limited ? "al menos " : "") + processed + " eventos relevantes en los últimos 7 días." : limited ? "La consulta quedó incompleta por su límite de tiempo." : "No se encontraron eventos relevantes recientes según las reglas conocidas.",
                DetailedDescription = "Solo registro System; últimos 7 días. Máximo 100 registros coincidentes y 20 detalles, con límite de lectura de 4 segundos. " +
                    "Se ignoran eventos informativos (incluido NTFS 98 de volumen sano) y eventos sin reglas conocidas. Los eventos históricos generan Atención, no un diagnóstico automático de avería actual." + (limited ? " La consulta alcanzó un límite; el recuento es parcial." : ""),
                Recommendation = events.Count > 0 ? "Revisar fechas, recurrencia y descripción antes de decidir cualquier acción." : "La ausencia de estas reglas no garantiza que todos los componentes estén sanos." };
        }
        private static string LevelName(byte? level)
        {
            switch (level) { case 1: return "Crítico"; case 2: return "Error"; case 3: return "Advertencia"; case 4: return "Información"; default: return "No disponible"; }
        }
    }
}
