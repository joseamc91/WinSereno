using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WinSereno.Models;

namespace WinSereno.Services
{
    public sealed class DiagnosticService
    {
        private readonly ISessionLogger logger;
        private readonly BasicDiagnosticChecks basic;
        private readonly IntegritySessionState integrity;
        public DiagnosticService(SystemInformationService information, ISessionLogger logger, IntegritySessionState integrity) { this.logger = logger; this.integrity = integrity; basic = new BasicDiagnosticChecks(information, logger); }
        public static IList<DiagnosticResult> CreatePendingResults()
        {
            var items = new List<DiagnosticResult>();
            foreach (var pair in new[] { Tuple.Create("space", "Espacio de almacenamiento"), Tuple.Create("storage-health", "Salud básica de almacenamiento"), Tuple.Create("restart", "Reinicio pendiente"),
                Tuple.Create("network", "Red local e Internet"), Tuple.Create("services", "Servicios críticos"), Tuple.Create("power", "Plan de energía"), Tuple.Create("events", "Eventos de Windows"), Tuple.Create("integrity", "Integridad de Windows") })
                items.Add(new DiagnosticResult { Id = pair.Item1, Name = pair.Item2, Status = DiagnosticStatus.NotChecked, Summary = pair.Item1 == "integrity" ? "La comprobación de integridad requiere una acción administrativa explícita." : "Aún no se ha realizado esta comprobación.",
                    Recommendation = pair.Item1 == "integrity" ? "Revisar las opciones de Reparación; no se ejecutará DISM en este análisis." : "Pulsar Analizar este PC.",
                    NavigationTarget = pair.Item1 == "integrity" ? (NavigationSection?)NavigationSection.Repair : null, NavigationLabel = pair.Item1 == "integrity" ? "Ir a Reparación" : null });
            return items;
        }
        public async Task RunAsync(IProgress<DiagnosticResult> progress, CancellationToken token)
        {
            var watch = Stopwatch.StartNew();
            SystemQuery.Log(logger, "Inicio de diagnóstico | Solo lectura | Sin elevación");
            try
            {
                foreach (var item in CreatePendingResults())
                {
                    token.ThrowIfCancellationRequested();
                    var result = await RunCheckAsync(item.Id, token).ConfigureAwait(false);
                    progress.Report(result);
                }
            }
            finally { SystemQuery.Log(logger, "Fin de diagnóstico | Duración=" + watch.Elapsed + " | Cancelación=" + token.IsCancellationRequested); }
        }
        // A future explicit revalidation can invoke one Id without duplicating orchestration or UI logic.
        public async Task<DiagnosticResult> RunCheckAsync(string id, CancellationToken token)
        {
            var watch = Stopwatch.StartNew();
            DiagnosticResult result;
            try
            {
                result = await Task.Run(async () =>
                {
                    token.ThrowIfCancellationRequested();
                    switch (id)
                    {
                        case "space": return basic.Space();
                        case "storage-health": return basic.StorageHealth();
                        case "restart": return basic.Restart();
                        case "network": return await new NetworkDiagnosticCheck(logger).RunAsync(token).ConfigureAwait(false);
                        case "services": return basic.Services();
                        case "power": return basic.Power();
                        case "events": return new EventDiagnosticCheck(logger).Run(token);
                        case "integrity": return integrity.Read();
                        default: throw new ArgumentException("Comprobación desconocida", nameof(id));
                    }
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                SystemQuery.Log(logger, "Error técnico diagnóstico " + id + ": " + ex);
                result = new DiagnosticResult { Id = id, Name = FindName(id), Status = DiagnosticStatus.NotChecked,
                    Summary = id == "storage-health" ? "El controlador o Windows no expone información de salud accesible como usuario normal." : "No se pudo obtener información fiable para esta comprobación.",
                    Recommendation = "No se ha solicitado elevación. El resto de comprobaciones puede continuar.",
                    NavigationTarget = id == "network" ? (NavigationSection?)NavigationSection.Network : null,
                    NavigationLabel = id == "network" ? "Ir a Red" : null };
            }
            result.Duration = watch.Elapsed;
            // Do not log TechnicalDetails or Windows event descriptions: they can contain private data/SSID.
            SystemQuery.Log(logger, "Comprobación " + id + " | Estado=" + result.Status + " | Duración=" + result.Duration + " | Resumen=" + result.Summary);
            return result;
        }
        private static string FindName(string id) { foreach (var item in CreatePendingResults()) if (item.Id == id) return item.Name; return id; }
    }
}
