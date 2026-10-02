using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using WinSereno.Infrastructure;
using WinSereno.Models;
using WinSereno.Services;
namespace WinSereno.ViewModels
{
    public sealed class NetworkViewModel : ObservableObject
    {
        private readonly ISessionLogger logger;
        private readonly OperationCoordinator operations;
        public ObservableCollection<NetworkAdapterInformation> Adapters { get; } = new ObservableCollection<NetworkAdapterInformation>();
        public RelayCommand RefreshCommand { get; }
        public MaintenanceTask FlushDnsTask { get; } = ElevatedTaskCatalog.Get(ElevatedTaskCatalog.FlushDnsId);
        public MaintenanceTask ResetTcpIpTask { get; } = ElevatedTaskCatalog.Get(ElevatedTaskCatalog.ResetTcpIpId);
        private MaintenanceTaskResult tcpIpResult;
        public string TcpIpResultText => tcpIpResult == null ? "Sin resultados durante esta sesión." : tcpIpResult.UserSummary + " · Duración: " + tcpIpResult.Duration.TotalSeconds.ToString("0.00") + " s";
        public void SetTcpIpResult(MaintenanceTaskResult result) { tcpIpResult = result; Raise(nameof(TcpIpResultText)); }
        public MaintenanceTask ResetWinsockTask { get; } = ElevatedTaskCatalog.Get(ElevatedTaskCatalog.ResetWinsockId);
        private MaintenanceTaskResult winsockResult;
        public string WinsockResultText => winsockResult == null ? "Sin resultados durante esta sesión." : winsockResult.UserSummary + " · Duración: " + winsockResult.Duration.TotalSeconds.ToString("0.00") + " s";
        public void SetWinsockResult(MaintenanceTaskResult result) { winsockResult = result; Raise(nameof(WinsockResultText)); }
        public MaintenanceTask RestartAdapterTask { get; } = ElevatedTaskCatalog.Get(ElevatedTaskCatalog.RestartAdapterId);
        private string restartResultText = "Sin resultados durante esta sesión.";
        public string RestartResultText { get => restartResultText; private set => Set(ref restartResultText, value); }
        public void SetRestartStatus(string text) { RestartResultText = text; }
        public void SetRestartResult(MaintenanceTaskResult result) { RestartResultText = result.UserSummary + " · Duración: " + result.Duration.TotalSeconds.ToString("0.00") + " s"; }
        public MaintenanceTask RenewDhcpTask { get; } = ElevatedTaskCatalog.Get(ElevatedTaskCatalog.RenewDhcpId);
        private string dhcpResultText = "Sin resultados durante esta sesión.";
        public string DhcpResultText { get => dhcpResultText; private set => Set(ref dhcpResultText, value); }
        public void SetDhcpStatus(string text) { DhcpResultText = text; }
        public void SetDhcpResult(MaintenanceTaskResult result) { DhcpResultText = result.UserSummary + " · Duración: " + result.Duration.TotalSeconds.ToString("0.00") + " s"; }
        private MaintenanceTaskResult flushDnsResult;
        public string FlushDnsResultText => flushDnsResult == null ? "Sin resultados durante esta sesión." : flushDnsResult.UserSummary + " · Duración: " + flushDnsResult.Duration.TotalSeconds.ToString("0.00") + " s";
        public void SetFlushDnsResult(MaintenanceTaskResult result) { flushDnsResult = result; Raise(nameof(FlushDnsResultText)); }
        private string summary = "Pendiente de consultar.";
        public string Summary { get => summary; private set => Set(ref summary, value); }
        private string connectivity;
        public string Connectivity { get => connectivity; private set { if (Set(ref connectivity, value)) Raise(nameof(HasConnectivityDetails)); } }
        public bool HasConnectivityDetails => !string.IsNullOrWhiteSpace(Connectivity);
        private string connectivityStatusCode = "NotChecked";
        public string ConnectivityStatusCode { get => connectivityStatusCode; private set { if (Set(ref connectivityStatusCode, value)) Raise(nameof(ConnectivityStatusText)); } }
        public string ConnectivityStatusText
        {
            get
            {
                switch (ConnectivityStatusCode)
                {
                    case "Healthy": return "Internet disponible";
                    case "Attention": return "Conexión con incidencias";
                    case "Error": case "Failed": return "Sin conexión a Internet";
                    default: return "Conexión no comprobada";
                }
            }
        }
        private DateTimeOffset? refreshedAt;
        public string RefreshedText => refreshedAt.HasValue ? "Última actualización: " + refreshedAt.Value.ToLocalTime().ToString("HH:mm:ss") : "Aún no se ha actualizado.";
        public NetworkViewModel(ISessionLogger logger, OperationCoordinator operations)
        {
            this.logger = logger; this.operations = operations;
            RefreshCommand = new RelayCommand(async p => await RefreshAsync(), p => !operations.IsActive);
            operations.Changed += (s, e) => RefreshCommand.Refresh();
        }
        public async Task RefreshAsync()
        {
            if (operations.IsActive) { Summary = "Hay otra operación activa. Pulsa Actualizar cuando termine."; return; }
            using (var operation = operations.Begin("Información de red", true))
            {
                var watch = Stopwatch.StartNew();
                Summary = "Consultando adaptadores y conectividad..."; Connectivity = null; ConnectivityStatusCode = "NotChecked";
                SystemQuery.Log(logger, "Inicio de actualización informativa de Red");
                try
                {
                    var service = new NetworkInformationService(logger);
                    try
                    {
                        var adapters = await Task.Run(() => service.ReadAdapters());
                        Adapters.Clear(); foreach (var adapter in adapters) Adapters.Add(adapter);
                    }
                    catch (Exception ex) { Adapters.Clear(); SystemQuery.Log(logger, "Listado de adaptadores no disponible: " + ex); Summary = "No se pudo consultar la lista de adaptadores físicos."; }
                    operation.Token.ThrowIfCancellationRequested();
                    var check = new NetworkDiagnosticCheck(logger);
                    var result = await Task.Run(() => check.RunAsync(operation.Token));
                    operation.Token.ThrowIfCancellationRequested();
                    Connectivity = check.ProbeDetails;
                    ConnectivityStatusCode = result.Status.ToString();
                    refreshedAt = DateTimeOffset.Now; Raise(nameof(RefreshedText));
                    Summary = result.StatusLabel + " · " + result.Summary;
                    if (Adapters.Count == 0) Summary += " · Sin adaptadores físicos disponibles para mostrar.";
                }
                catch (OperationCanceledException) { Summary = "Consulta de red cancelada."; }
                catch (Exception ex) { Summary = "No se pudo completar la consulta de red."; SystemQuery.Log(logger, "Error consulta de Red: " + ex); }
                finally { SystemQuery.Log(logger, "Fin de actualización informativa de Red | Duración=" + watch.Elapsed); }
            }
        }
    }
}
