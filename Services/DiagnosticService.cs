using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinSereno.Models;

namespace WinSereno.Services
{
    public sealed class DiagnosticService
    {
        private readonly ISessionLogger logger;
        private readonly BasicDiagnosticChecks basic;
        private readonly Func<OperationLease, Func<Task>, Action<TaskProgress>, Task<MaintenanceTaskResult>> elevatedIntegrity;
        private readonly Func<string, CancellationToken, Task<DiagnosticResult>> readCheck;
        public DiagnosticService(SystemInformationService information, ISessionLogger logger, IntegritySessionState integrity,
            Func<OperationLease, Func<Task>, Action<TaskProgress>, Task<MaintenanceTaskResult>> elevatedIntegrity = null,
            Func<string, CancellationToken, Task<DiagnosticResult>> readCheck = null)
        { this.logger = logger; basic = new BasicDiagnosticChecks(information, logger); this.elevatedIntegrity = elevatedIntegrity; this.readCheck = readCheck; }
        public static IList<DiagnosticResult> CreatePendingResults()
        {
            var items = new List<DiagnosticResult>();
            foreach (var pair in new[] {
                Tuple.Create("space", "Espacio de almacenamiento", "Comprueba si las unidades tienen suficiente espacio libre."),
                Tuple.Create("storage-health", "Salud básica de almacenamiento", "Consulta el estado básico que Windows informa de los discos físicos."),
                Tuple.Create("network", "Red local e Internet", "Comprueba si el equipo tiene red activa y acceso funcional a Internet."),
                Tuple.Create("services", "Servicios críticos", "Comprueba que los servicios esenciales de Windows estén funcionando."),
                Tuple.Create("events", "Eventos de Windows", "Busca errores o avisos recientes relevantes en los registros de Windows."),
                Tuple.Create("integrity", "Integridad de Windows", "Comprueba el almacén de componentes y los archivos protegidos de Windows sin repararlos.") })
                items.Add(new DiagnosticResult { Id = pair.Item1, Name = pair.Item2, Status = DiagnosticStatus.NotChecked, Summary = pair.Item3 });
            return items;
        }
        public async Task RunAsync(IProgress<DiagnosticResult> progress, OperationLease operation, IProgress<TaskProgress> phase)
        {
            var token = operation.Token;
            var watch = Stopwatch.StartNew();
            SystemQuery.Log(logger, "Inicio de diagnóstico | Solo lectura | Integridad mediante un worker elevado");
            try
            {
                Func<Task> normal = async () =>
                {
                    phase.Report(new TaskProgress { State = RunnerState.Running, LastRelevantLine = "Analizando..." });
                    foreach (var item in CreatePendingResults().Where(r => r.Id != "integrity"))
                    {
                        token.ThrowIfCancellationRequested();
                        DiagnosticResult result;
                        try { result = await (readCheck == null ? RunCheckAsync(item.Id, token) : readCheck(item.Id, token)).ConfigureAwait(false); }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { SystemQuery.Log(logger, "Error técnico diagnóstico " + item.Id + ": " + ex); result = item; result.Summary = "No se pudo obtener información fiable para esta comprobación."; }
                        progress.Report(result);
                    }
                };
                MaintenanceTaskResult integrityResult;
                if (elevatedIntegrity == null)
                {
                    await normal().ConfigureAwait(false);
                    integrityResult = new MaintenanceTaskResult { ExecutionStatus = ExecutionStatus.Failed, UserSummary = "No está disponible el transporte administrativo de integridad." };
                }
                else integrityResult = await elevatedIntegrity(operation, normal, value => phase.Report(value)).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                DiagnosticIntegritySequence.CompleteMissing(integrityResult);
                var integrityCard = DiagnosticIntegritySequence.ToDiagnostic(integrityResult);
                progress.Report(integrityCard);
                SystemQuery.Log(logger, "Integridad combinada | Estado=" + integrityCard.Status + " | " + integrityCard.Summary);
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
                        case "network": return await new NetworkDiagnosticCheck(logger).RunAsync(token).ConfigureAwait(false);
                        case "services": return basic.Services();
                        case "events": return new EventDiagnosticCheck(logger).Run(token);
                        case "integrity": throw new InvalidOperationException("Integridad se ejecuta dentro del diagnóstico general mediante su secuencia fija de solo lectura.");
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
