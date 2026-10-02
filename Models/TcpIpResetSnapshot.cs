using System;
using System.Collections.Generic;
using System.Linq;

namespace WinSereno.Models
{
    public enum Ipv4ConfigurationMode { Dhcp, Manual, Unknown }

    public sealed class TcpIpAdapterConfiguration
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public Ipv4ConfigurationMode Mode { get; }
        public string IPv4 { get; }
        public string Mask { get; }
        public string Gateway { get; }
        public string Dns { get; }
        public string Heading => Name == Description || string.IsNullOrWhiteSpace(Description) ? Name : Name + " · " + Description;
        public string ConfigurationLabel => Mode == Ipv4ConfigurationMode.Manual ? "DHCP desactivado · Configuración manual"
            : Mode == Ipv4ConfigurationMode.Dhcp ? "DHCP activado" : "Configuración no disponible";
        public TcpIpAdapterConfiguration(string id, string name, string description, Ipv4ConfigurationMode mode,
            string ipv4, string mask, string gateway, string dns)
        {
            Id = id ?? ""; Name = name ?? "No disponible"; Description = description; Mode = mode;
            IPv4 = Available(ipv4); Mask = Available(mask); Gateway = Available(gateway); Dns = Available(dns);
        }
        private static string Available(string value) => string.IsNullOrWhiteSpace(value) ? "No disponible" : value;
    }

    public sealed class TcpIpResetSnapshot
    {
        public IReadOnlyList<TcpIpAdapterConfiguration> Interfaces { get; }
        public IReadOnlyList<TcpIpAdapterConfiguration> WarningInterfaces { get; }
        public bool ReadComplete { get; }
        public bool RequiresWarning => !ReadComplete || Interfaces.Any(a => a.Mode != Ipv4ConfigurationMode.Dhcp);
        public bool IsIndeterminate => !ReadComplete || Interfaces.Any(a => a.Mode == Ipv4ConfigurationMode.Unknown);
        public string WarningTitle => IsIndeterminate ? "Configuración IPv4 no comprobada completamente" : "Configuración IPv4 manual detectada";
        public string WarningMessage => IsIndeterminate
            ? "No se pudo comprobar completamente la configuración IPv4 de este equipo. Restablecer TCP/IP puede modificar la configuración existente y dejar el equipo sin conexión."
            : WarningInterfaces.Count == 1
                ? "Esta interfaz utiliza una configuración IPv4 manual. Restablecer TCP/IP puede modificarla y dejar el equipo sin conexión."
                : "Se han detectado interfaces con configuración IPv4 manual. Restablecer TCP/IP puede modificarlas y dejar el equipo sin conexión.";
        public string UnavailableNotice => IsIndeterminate ? "Configuración no disponible o incompleta; no se presupone que las interfaces utilicen DHCP." : "";
        public TcpIpResetSnapshot(IEnumerable<TcpIpAdapterConfiguration> interfaces, bool readComplete)
        {
            Interfaces = Array.AsReadOnly(interfaces.ToArray()); ReadComplete = readComplete;
            WarningInterfaces = Array.AsReadOnly(Interfaces.Where(a => a.Mode != Ipv4ConfigurationMode.Dhcp).ToArray());
        }
    }
}
