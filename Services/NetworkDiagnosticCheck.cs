using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinSereno.Models;

namespace WinSereno.Services
{
    internal sealed class ConnectivityProbe
    {
        public string Name { get; set; }
        public bool Success { get; set; }
        public bool TimedOut { get; set; }
        public string Detail { get; set; }
        public TimeSpan Duration { get; set; }
    }
    internal sealed class NetworkDiagnosticCheck
    {
        private const string DnsHost = "www.microsoft.com";
        private const string HttpsEndpoint = "https://www.microsoft.com/";
        private const string PublicIp = "1.1.1.1";
        private readonly ISessionLogger logger;
        public string ProbeDetails { get; private set; }
        public NetworkDiagnosticCheck(ISessionLogger logger) { this.logger = logger; }
        public async Task<DiagnosticResult> RunAsync(CancellationToken token)
        {
            var adapter = new NetworkInformationService(logger).Read();
            var details = new StringBuilder();
            bool linkWarning = false;
            if (adapter.NeutralMessage == null)
            {
                details.AppendLine("Adaptador conectado: " + adapter.Kind + " · " + adapter.Description);
                details.AppendLine("IPv4: " + (adapter.IPv4 ?? "no determinada"));
                details.AppendLine("Gateway IPv4: " + (adapter.GatewayIPv4 ?? "no determinado"));
                details.AppendLine("DNS configurados: " + (adapter.DnsServers.Length > 0 ? string.Join(", ", adapter.DnsServers) : "no disponibles"));
                details.AppendLine("Velocidad negociada: " + (adapter.SpeedBitsPerSecond.HasValue ? Speed(adapter.SpeedBitsPerSecond.Value) : "no disponible"));
                if (adapter.Kind == "Ethernet")
                {
                    long? maximum = new NetworkInformationService(logger).ReadMaximumSpeed(adapter.InterfaceId);
                    details.AppendLine("Capacidad máxima publicada: " + (maximum.HasValue ? Speed(maximum.Value) : "no disponible; no se infiere del nombre comercial"));
                    linkWarning = adapter.SpeedBitsPerSecond == 100000000 && maximum > 100000000;
                    if (linkWarning) details.AppendLine("El adaptador admite velocidades superiores, pero negocia a 100 Mbps. Puede relacionarse con cable, puerto del router/switch, negociación, configuración o controlador; no se ha determinado la causa.");
                }
                else if (adapter.Kind == "Wi-Fi")
                {
                    try
                    {
                        var wifi = NativeWifiInformation.Read(adapter.InterfaceId);
                        details.AppendLine(wifi == null ? "SSID y señal: no disponibles" : "SSID: " + (wifi.Ssid ?? "no disponible; no se solicitan permisos de ubicación") +
                            "\nSeñal: " + (wifi.SignalQuality.HasValue ? wifi.SignalQuality + " %" : wifi.Rssi.HasValue ? wifi.Rssi + " dBm" : "no disponible") +
                            "\nEnlace Wi-Fi recepción/envío: " + (wifi.ReceiveRate > 0 ? Speed(wifi.ReceiveRate * 1000L) : "no disponible") + " / " + (wifi.TransmitRate > 0 ? Speed(wifi.TransmitRate * 1000L) : "no disponible"));
                    }
                    catch (Exception ex) { details.AppendLine("SSID y señal: no disponibles (Windows o el controlador limitan la consulta)"); SystemQuery.Log(logger, "Consulta Wi-Fi no disponible: " + ex.GetType().Name + " | Código=" + (ex is System.ComponentModel.Win32Exception native ? native.NativeErrorCode.ToString() : "no disponible")); }
                }
            }
            else details.AppendLine(adapter.NeutralMessage);
            token.ThrowIfCancellationRequested();
            // Independent probes run concurrently; their outcomes are combined, never inferred from ICMP alone.
            var gatewayTask = PingAsync("Gateway", adapter.GatewayIPv4, 1500);
            var publicTask = PingAsync("IP pública", PublicIp, 1800);
            var dnsTask = DnsAsync();
            var httpsTask = HttpsAsync();
            var probes = await Task.WhenAll(gatewayTask, publicTask, dnsTask, httpsTask).ConfigureAwait(false);
            var probeDetails = new StringBuilder();
            if (adapter.NeutralMessage != null) probeDetails.AppendLine(adapter.NeutralMessage);
            if (linkWarning) probeDetails.AppendLine("El enlace Ethernet negocia a 100 Mbps pese a admitir una velocidad superior. No se ha determinado la causa.");
            foreach (var probe in probes)
            {
                details.AppendLine((probe.Success ? "✓ " : "— ") + probe.Name + ": " + probe.Detail);
                probeDetails.AppendLine((probe.Success ? "✓ " : "— ") + probe.Name + ": " + probe.Detail + " · " + probe.Duration.TotalMilliseconds.ToString("0", CultureInfo.CurrentCulture) + " ms");
                SystemQuery.Log(logger, "Red prueba " + probe.Name + ": " + probe.Detail + " | Duración=" + probe.Duration);
            }
            ProbeDetails = probeDetails + "\nLa ausencia de respuesta ICMP no demuestra una avería. Un único endpoint no representa todo Internet.";
            var result = Summarize(adapter.NeutralMessage != null, linkWarning, probes, details.ToString());
            SystemQuery.Log(logger, "Resultado general de red: " + result.Status + " | HTTPS=" + probes[3].Success + " | Timeout HTTPS=" + probes[3].TimedOut + " | DNS=" + probes[2].Success + " | ICMP gateway=" + probes[0].Success + " | ICMP público=" + probes[1].Success + " | Aviso Ethernet=" + linkWarning);
            return result;
        }
        private static DiagnosticResult Summarize(bool adapterUnavailable, bool linkWarning, ConnectivityProbe[] probes, string details)
        {
            bool https = probes[3].Success;
            // A single endpoint timeout is inconclusive when all three independent signals agree.
            // Certificate, proxy and other HTTPS failures retain their warning even with ICMP/DNS responses.
            bool supportedTimeout = !https && probes[3].TimedOut && probes.Take(3).All(p => p.Success);
            bool internetAvailable = https || supportedTimeout;
            string summary = https ? "Conectividad funcional de Internet confirmada mediante HTTPS." : supportedTimeout
                ? "Internet disponible según gateway, IP pública y DNS. La comprobación HTTPS específica no respondió dentro del tiempo esperado."
                : "No se pudo confirmar HTTPS con el endpoint de Microsoft. Revisa las demás pruebas de conectividad.";
            if (internetAvailable && linkWarning)
                summary = "Internet disponible; enlace Ethernet a 100 Mbps con capacidad superior confirmada." + (supportedTimeout ? " La comprobación HTTPS específica no respondió dentro del tiempo esperado." : "");
            var result = new DiagnosticResult { Id = "network", Name = "Red local e Internet",
                Status = internetAvailable && !linkWarning ? DiagnosticStatus.Healthy : DiagnosticStatus.Attention,
                Summary = summary,
                DetailedDescription = details + "\nLa ausencia de respuesta ICMP no demuestra una avería. Un único endpoint tampoco representa todo Internet.",
                Recommendation = internetAvailable ? "Sin reparación automática. Las pruebas ICMP pueden estar bloqueadas por la red." : "Revisar los resultados de DNS, HTTPS y gateway antes de atribuir una causa. No se ha modificado la red.",
                NavigationTarget = NavigationSection.Network, NavigationLabel = "Ir a Red" };
            if (adapterUnavailable && probes.All(p => !p.Success))
            { result.Status = DiagnosticStatus.NotChecked; result.Summary = "No se obtuvo información suficiente para valorar la conectividad."; }
            return result;
        }
        private async Task<ConnectivityProbe> PingAsync(string name, string address, int timeout)
        {
            var watch = Stopwatch.StartNew();
            if (address == null) return new ConnectivityProbe { Name = name, Detail = "destino no determinado" };
            try
            {
                using (var ping = new Ping())
                {
                    var response = await ping.SendPingAsync(address, timeout).ConfigureAwait(false);
                    return new ConnectivityProbe { Name = name, Success = response.Status == IPStatus.Success, Detail = response.Status == IPStatus.Success ? "respuesta ICMP recibida" : "sin respuesta ICMP válida (" + response.Status + ")", Duration = watch.Elapsed };
                }
            }
            catch (Exception ex) { SystemQuery.Log(logger, "Error técnico prueba " + name + ": " + ex.GetType().Name + " | " + ex.Message); return new ConnectivityProbe { Name = name, Detail = "ICMP no comprobado", Duration = watch.Elapsed }; }
        }
        private async Task<ConnectivityProbe> DnsAsync()
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var pending = Dns.GetHostAddressesAsync(DnsHost);
                if (await Task.WhenAny(pending, Task.Delay(3000)).ConfigureAwait(false) != pending)
                {
                    ObserveFault(pending);
                    return new ConnectivityProbe { Name = "DNS", Detail = "timeout de resolución (3 s); no comprobado", Duration = watch.Elapsed };
                }
                var addresses = await pending.ConfigureAwait(false);
                return new ConnectivityProbe { Name = "DNS", Success = addresses.Length > 0, Detail = addresses.Length > 0 ? DnsHost + " resuelto" : "sin direcciones; no comprobado", Duration = watch.Elapsed };
            }
            catch (Exception ex) { SystemQuery.Log(logger, "Error técnico DNS: " + ex.GetType().Name + " | " + ex.Message); return new ConnectivityProbe { Name = "DNS", Detail = "no se pudo resolver el hostname; resultado no concluyente", Duration = watch.Elapsed }; }
        }
        private async Task<ConnectivityProbe> HttpsAsync()
        {
            var watch = Stopwatch.StartNew();
            var request = (HttpWebRequest)WebRequest.Create(HttpsEndpoint);
            request.Method = "HEAD"; request.Timeout = 5000; request.ReadWriteTimeout = 5000;
            request.AllowAutoRedirect = false; request.KeepAlive = false;
            request.Credentials = null;
            try
            {
                var pending = request.GetResponseAsync();
                if (await Task.WhenAny(pending, Task.Delay(5000)).ConfigureAwait(false) != pending)
                {
                    request.Abort(); ObserveFault(pending);
                    return HttpsTimeout(watch.Elapsed);
                }
                using (var response = (HttpWebResponse)await pending.ConfigureAwait(false))
                {
                    return HttpsResponse((int)response.StatusCode, response.ResponseUri, watch.Elapsed);
                }
            }
            catch (WebException ex)
            {
                // An HTTP rejection after TLS is still evidence of HTTPS connectivity, not a transport failure.
                if (ex.Status == WebExceptionStatus.ProtocolError && ex.Response is HttpWebResponse response)
                {
                    using (response)
                    {
                        return HttpsResponse((int)response.StatusCode, response.ResponseUri, watch.Elapsed);
                    }
                }
                ex.Response?.Dispose();
                SystemQuery.Log(logger, "Error técnico HTTPS: " + ex.Status);
                if (ex.Status == WebExceptionStatus.Timeout) return HttpsTimeout(watch.Elapsed);
                return new ConnectivityProbe { Name = "HTTPS", Detail = "no confirmado (" + ex.Status + ")", Duration = watch.Elapsed };
            }
        }
        private static ConnectivityProbe HttpsTimeout(TimeSpan duration) => new ConnectivityProbe {
            Name = "HTTPS", TimedOut = true, Detail = "timeout de conexión (5 s); no comprobado", Duration = duration };
        private static ConnectivityProbe HttpsResponse(int code, Uri endpoint, TimeSpan duration)
        {
            bool expectedEndpoint = endpoint != null && endpoint.Scheme == "https" && endpoint.Host == new Uri(HttpsEndpoint).Host;
            return new ConnectivityProbe { Name = "HTTPS", Success = code != 407 && expectedEndpoint,
                Detail = expectedEndpoint ? "respuesta HTTPS recibida de Microsoft · HTTP " + code + (code >= 400 ? " (el endpoint rechaza la petición HEAD)" : "") : "respuesta de un endpoint distinto; HTTPS no confirmado",
                Duration = duration };
        }
        private static void ObserveFault(Task task) => task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        private static string Speed(long speed) => speed >= 1000000000 ? (speed / 1000000000.0).ToString("0.##", CultureInfo.CurrentCulture) + " Gbps" : (speed / 1000000.0).ToString("0.##", CultureInfo.CurrentCulture) + " Mbps";
    }
}
