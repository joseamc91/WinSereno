using System;
using Microsoft.Win32;
using WinSereno.Models;

namespace WinSereno.Services
{
    public sealed class RestartPendingService
    {
        private readonly ISessionLogger logger;
        public RestartPendingService(ISessionLogger logger) { this.logger = logger; }
        public RestartPendingInformation Read()
        {
            var result = new RestartPendingInformation();
            bool unknown = false;
            Check("CBS RebootPending", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending", null, result, ref unknown);
            Check("Windows Update RebootRequired", @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired", null, result, ref unknown);
            Check("PendingFileRenameOperations", @"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations", result, ref unknown);
            bool strong = result.PendingIndicators.Contains("CBS RebootPending") || result.PendingIndicators.Contains("Windows Update RebootRequired");
            result.Status = strong ? RestartPendingStatus.Pending : unknown ? RestartPendingStatus.Unknown : result.PendingIndicators.Count > 0 ? RestartPendingStatus.Possible : RestartPendingStatus.NotPending;
            SystemQuery.Log(logger, "Reinicio pendiente: " + result.Status + " | Indicadores positivos: " + string.Join(", ", result.PendingIndicators));
            return result;
        }
        private void Check(string name, string path, string value, RestartPendingInformation result, ref bool unknown)
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
                using (var key = root.OpenSubKey(path, false))
                {
                    var rawValue = value == null ? null : key?.GetValue(value, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    bool pending = value == null ? key != null : rawValue is string[] operations && Array.Exists(operations, s => !string.IsNullOrWhiteSpace(s));
                    if (value != null && key == null) { unknown = true; SystemQuery.Log(logger, "Reinicio indicador " + name + ": no disponible"); return; }
                    if (value != null && rawValue != null && !(rawValue is string[])) { unknown = true; SystemQuery.Log(logger, "Reinicio indicador " + name + ": tipo inesperado"); return; }
                    if (pending) result.PendingIndicators.Add(name);
                    SystemQuery.Log(logger, "Reinicio indicador " + name + ": " + (pending ? "presente" : "ausente"));
                }
            }
            catch (Exception ex) { unknown = true; SystemQuery.Log(logger, "Error reinicio indicador " + name + ": " + ex); }
        }
    }
}
