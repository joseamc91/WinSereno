using System;
using System.IO;
using WinSereno.Models;

namespace WinSereno.Services
{
    public static class ElevatedTaskCatalog
    {
        public const string DiagnosticIntegrityId = "diagnosis.integrity";
        public const string SfcVerifyOnlyId = "diagnosis.sfc.verifyonly";
        public const string CleanupSelectedId = "cleanup.selected";
        public const string WindowsTempCleanupId = "cleanup.windowstemp";
        public const string WindowsTempAnalyzeId = "cleanup.windowstemp.analyze";
        public const string ComponentCleanupId = "repair.dism.componentcleanup";
        public const string ChkdskId = "repair.chkdsk.scan";
        public const string CheckHealthId = "repair.dism.checkhealth";
        public const string CheckHealthArguments = "/Online /Cleanup-Image /CheckHealth /English";
        public const string ScanHealthId = "repair.dism.scanhealth";
        public const string RestoreHealthId = "repair.dism.restorehealth";
        public const string SfcId = "repair.sfc.scannow";
        public const string ResetTcpIpId = "network.resettcpip";
        public const string ResetWinsockId = "network.resetwinsock";
        public const string RestartAdapterId = "network.restartadapter";
        public const string RenewDhcpId = "network.renewdhcp";
        public const string FlushDnsId = "network.flushdns";
        public const string CompleteId = "repair.complete";
        public static bool IsAllowed(string id) => id == DiagnosticIntegrityId || id == SfcVerifyOnlyId || id == ComponentCleanupId || id == ChkdskId || id == CheckHealthId || id == ScanHealthId || id == RestoreHealthId || id == SfcId || id == CompleteId || id == FlushDnsId || id == RenewDhcpId || id == RestartAdapterId || id == ResetWinsockId || id == ResetTcpIpId || id == WindowsTempAnalyzeId || id == WindowsTempCleanupId || id == CleanupSelectedId;
        public static MaintenanceTask Get(string id)
        {
            if (!IsAllowed(id)) throw new ArgumentException("TaskId administrativo no permitido.");
            if (id == DiagnosticIntegrityId)
                return new MaintenanceTask { Id = id, Name = "Comprobar integridad de Windows", Category = TaskCategory.Diagnosis,
                    TaskType = TaskType.Internal, ImpactLevel = ImpactLevel.Information, RequiresElevation = true, CanBeCancelled = false,
                    Command = Get(CheckHealthId).Command + " " + CheckHealthArguments + "\n" + Get(SfcVerifyOnlyId).Command + " /verifyonly",
                    Arguments = "Secuencia fija de solo lectura; sin argumentos externos." };
            if (id == SfcVerifyOnlyId)
                return new MaintenanceTask { Id = id, Name = "Verificar archivos protegidos", Category = TaskCategory.Diagnosis,
                    TaskType = TaskType.Command, ImpactLevel = ImpactLevel.Information, RequiresElevation = true, CanBeCancelled = false,
                    Command = SystemPath("sfc.exe"), Arguments = "/verifyonly", MayRequireRestart = false };
            if (id == ComponentCleanupId)
                return new MaintenanceTask { Id = id, Name = "Limpiar almacén de componentes", Category = TaskCategory.Repair,
                    ShortDescription = "Elimina versiones reemplazadas de componentes de Windows.",
                    DetailedDescription = "Windows eliminará versiones reemplazadas de componentes que ya no necesita. Es una acción de mantenimiento, no una reparación de archivos dañados. Puede tardar varios minutos; no se promete una cantidad concreta de espacio recuperado. Requiere administrador. No reiniciará el equipo automáticamente. Esta versión no usa /ResetBase ni ninguna opción adicional de limpieza. Una vez iniciada, se espera a que termine sin forzar cancelación.",
                    TaskType = TaskType.Command, ImpactLevel = ImpactLevel.Maintenance, Command = DismPath,
                    Arguments = "/Online /Cleanup-Image /StartComponentCleanup /English", RequiresElevation = true, CanBeCancelled = false, MayRequireRestart = true };
            if (id == ChkdskId)
            {
                string volume = WindowsVolumeResolver.Resolve();
                return new MaintenanceTask { Id = id, Name = "Comprobar disco (CHKDSK)", Category = TaskCategory.Repair,
                    ShortDescription = "Comprobación de solo lectura del sistema de archivos de " + volume + ".",
                    DetailedDescription = "Comprueba el sistema de archivos de " + volume + ", la unidad de la instalación actual de Windows. Es una comprobación de solo lectura y puede tardar. No repara, no programa comprobaciones al reiniciar, no modifica el dirty bit y no reinicia ni apaga el equipo. Al comprobar una partición activa sin bloquearla, pueden aparecer inconsistencias que necesiten verificación posterior; un aviso no demuestra por sí solo un daño definitivo. Requiere administrador. Una vez iniciada, se espera a que termine sin forzar cancelación.",
                    TaskType = TaskType.Command, ImpactLevel = ImpactLevel.Information, Command = SystemPath("chkdsk.exe"), Arguments = volume,
                    RequiresElevation = true, CanBeCancelled = false, MayRequireRestart = false };
            }
            if (id == CleanupSelectedId)
                return new MaintenanceTask { Id = id, Name = "Limpiar seleccionados", Category = TaskCategory.Cleanup, TaskType = TaskType.Internal,
                    ImpactLevel = ImpactLevel.Maintenance, RequiresElevation = true, CanBeCancelled = false,
                    Command = "Limpieza interna de categorías conocidas", Arguments = "Sin rutas, comandos ni patrones arbitrarios" };
            if (id == WindowsTempCleanupId)
                return new MaintenanceTask { Id = id, Name = "Limpiar temporales de Windows", Category = TaskCategory.Cleanup,
                    ShortDescription = "Se eliminarán archivos temporales que ya no sean necesarios.",
                    DetailedDescription = "Ruta: " + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") +
                        "\nLa carpeta raíz se conserva. Se revalidan fecha y atributos justo antes de borrar. No se siguen junctions, symlinks ni puntos de análisis. Los archivos en uso, bloqueados o inaccesibles se omiten; no se cambia ningún permiso ni se matan procesos. Solo se borran carpetas vacías elegibles. Después se reanaliza esta misma carpeta dentro del mismo worker, sin un segundo UAC. Requiere administrador; no se fuerza la cancelación.",
                    TaskType = TaskType.Internal, ImpactLevel = ImpactLevel.Maintenance, RequiresElevation = true, CanBeCancelled = false,
                    Command = "Limpieza interna .NET de temporales de Windows", Arguments = "Ruta resuelta internamente; archivos en uso o protegidos se omiten; reanálisis posterior de solo lectura" };
            if (id == WindowsTempAnalyzeId)
                return new MaintenanceTask { Id = id, Name = "Analizar temporales de Windows", Category = TaskCategory.Cleanup,
                    ShortDescription = "Análisis exclusivamente de lectura con permisos de administrador.",
                    DetailedDescription = "Ruta: " + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") +
                        "\nCalcula el tamaño accesible, número de archivos y tamaño potencialmente limpiable de los archivos elegibles. No borra ningún archivo ni carpeta, no cambia permisos y no sigue puntos de análisis. Los elementos inaccesibles se omiten y se indica un resultado parcial. La estimación no garantiza el espacio que podría recuperarse. Solicita un único UAC para el mismo ejecutable; la aplicación principal permanece sin privilegios.",
                    TaskType = TaskType.Internal, ImpactLevel = ImpactLevel.Information, RequiresElevation = true, CanBeCancelled = false,
                    Command = "Análisis interno .NET de temporales de Windows (solo lectura)", Arguments = "Sin procesos externos ni rutas proporcionadas por la interfaz" };
            if (id == ResetTcpIpId)
                return new MaintenanceTask { Id = id, Name = "Restablecer TCP/IP", Category = TaskCategory.Network,
                    ShortDescription = "Restablece parámetros de la pila TCP/IP y puede modificar la configuración de red.",
                    DetailedDescription = "Realiza cambios reales en parámetros TCP/IP y DHCP de Windows y puede modificar la configuración de red, incluidas direcciones IP y otros ajustes existentes. Puede interrumpir la conectividad, incluidas sesiones RDP. Normalmente será necesario reiniciar Windows para completar el restablecimiento. La aplicación nunca reinicia automáticamente. Requiere permisos de administrador y solicita un único UAC después de confirmar. No se combina con Winsock ni con otra herramienta. Si hay fallos parciales se conservan y explican; no se intenta corregirlos automáticamente. No se cancela a la fuerza y se espera a que termine antes de cerrar la aplicación.",
                    ImpactLevel = ImpactLevel.Configuration, TaskType = TaskType.Command, Command = SystemPath("netsh.exe"), Arguments = "int ip reset",
                    RequiresElevation = true, CanBeCancelled = false, MayRequireRestart = true };
            if (id == ResetWinsockId)
                return new MaintenanceTask { Id = id, Name = "Restablecer Winsock", Category = TaskCategory.Network,
                    ShortDescription = "Restablece el catálogo Winsock utilizado por las aplicaciones para comunicarse por la red.",
                    DetailedDescription = "Realiza cambios reales: restablece el catálogo Winsock a un estado limpio y puede eliminar proveedores LSP personalizados. Puede afectar temporalmente a aplicaciones y a la conectividad, incluidas sesiones remotas. No restablece TCP/IP ni cambia manualmente IP o servidores DNS. Puede ser necesario reiniciar Windows para completar el cambio. La aplicación nunca reinicia automáticamente. Requiere permisos de administrador; se solicita un único UAC después de confirmar. La operación no se cancela a la fuerza y debe terminar antes de cerrar la aplicación.",
                    ImpactLevel = ImpactLevel.Configuration, TaskType = TaskType.Command, Command = SystemPath("netsh.exe"), Arguments = "winsock reset",
                    RequiresElevation = true, CanBeCancelled = false, MayRequireRestart = true };
            if (id == RestartAdapterId)
                return new MaintenanceTask { Id = id, Name = "Reiniciar adaptador de red", Category = TaskCategory.Network,
                    ShortDescription = "Deshabilita y vuelve a habilitar un adaptador físico activo Ethernet o Wi-Fi.",
                    DetailedDescription = "Se perderá temporalmente la conectividad del adaptador seleccionado, incluidas las sesiones RDP. Requiere administrador. No cambia manualmente IP ni DNS; no restablece Winsock ni TCP/IP. La reconexión puede volver a negociar el enlace y DHCP. Solo continúa el reinicio si Windows confirma el deshabilitado. Si enable falla, se intenta habilitar hasta tres veces y se informa cómo recuperarlo manualmente. No reinicia el equipo y no se permite cerrar la aplicación ni cancelar a la fuerza durante la operación.",
                    ImpactLevel = ImpactLevel.Configuration, TaskType = TaskType.Internal, RequiresElevation = true, CanBeCancelled = false,
                    Command = "(requiere selección interna de adaptador físico activo)", Arguments = "(ningún comando sin selección y confirmación)" };
            if (id == RenewDhcpId)
                return new MaintenanceTask { Id = id, Name = "Renovar dirección DHCP", Category = TaskCategory.Network,
                    ShortDescription = "Libera y renueva la dirección IPv4 de interfaces físicas activas configuradas por DHCP.",
                    DetailedDescription = "Puede perderse temporalmente la conexión, incluidas sesiones remotas. No vacía la caché DNS ni cambia manualmente DNS; no restablece Winsock ni la configuración TCP/IP. DHCP puede entregar una dirección, gateway o servidores DNS distintos. Se intenta sin elevación y solo una denegación explícita solicita UAC para esta misma selección confirmada. Si release falla o no puede confirmarse, renew se omite para esa interfaz. No se reinicia el equipo y no se fuerza la cancelación.",
                    ImpactLevel = ImpactLevel.Configuration, TaskType = TaskType.Internal,
                    Command = "(requiere selección interna de interfaces DHCP)", Arguments = "(ningún comando sin selección y confirmación)",
                    RequiresElevation = false, CanBeCancelled = false, MayRequireRestart = false };
            if (id == FlushDnsId)
                return new MaintenanceTask { Id = id, Name = "Vaciar caché DNS", Category = TaskCategory.Network,
                    ShortDescription = "Elimina las respuestas DNS almacenadas temporalmente por Windows.",
                    DetailedDescription = "Vacía la caché local de resolución DNS, incluidas respuestas negativas. Las siguientes consultas podrán obtener respuestas nuevas. No cambia servidores DNS, IP, gateway ni DHCP; no reinicia adaptadores, no limpia la caché del navegador y no garantiza resolver un problema de Internet. Se ejecuta primero con permisos normales. Solo si Windows deniega explícitamente los permisos se solicita UAC para esta misma acción fija, después de esta confirmación. No requiere reinicio. Es una operación breve y se espera a que termine sin forzar cancelación.",
                    ImpactLevel = ImpactLevel.Maintenance, TaskType = TaskType.Command, Command = SystemPath("ipconfig.exe"), Arguments = "/flushdns",
                    RequiresElevation = false, CanBeCancelled = false, MayRequireRestart = false };
            if (id == CompleteId)
                return new MaintenanceTask { Id = id, Name = "Reparación completa", Category = TaskCategory.Repair,
                    ShortDescription = "ScanHealth → RestoreHealth solo si hay corrupción reparable → SFC.",
                    DetailedDescription = "1. Analizar imagen de Windows — ScanHealth.\n2. Reparar imagen si se detecta corrupción — RestoreHealth (condicional).\n3. Comprobar archivos del sistema — SFC.\nSi ScanHealth devuelve Healthy, se omite RestoreHealth. Si devuelve RepairRequired, se repara y solo se continúa con SFC si RestoreHealth devuelve Repaired o Healthy. Un fallo, resultado desconocido o cancelación detiene la secuencia y explica los pasos omitidos. RestoreHealth y SFC realizan cambios reales. Puede tardar varios minutos. Se solicita un único UAC; no se reinicia automáticamente y no se fuerza la cancelación.",
                    ImpactLevel = ImpactLevel.Repair, TaskType = TaskType.Internal, RequiresElevation = true,
                    CanBeCancelled = false, MayRequireRestart = true,
                    Command = Get(ScanHealthId).Command + " " + Get(ScanHealthId).Arguments + "\n" +
                        "[Solo si ScanHealth = RepairRequired]\n" + Get(RestoreHealthId).Command + " " + Get(RestoreHealthId).Arguments + "\n" +
                        Get(SfcId).Command + " " + Get(SfcId).Arguments, Arguments = "Secuencia fija interna; no admite argumentos externos." };
            if (id != CheckHealthId)
            {
                bool scan = id == ScanHealthId, sfc = id == SfcId;
                return new MaintenanceTask { Id = id, Name = scan ? "Analizar imagen de Windows" : sfc ? "Comprobar archivos del sistema" : "Reparar imagen de Windows",
                    ShortDescription = scan ? "Análisis profundo sin reparación del almacén de componentes." : sfc ? "Comprobación y reparación de archivos protegidos del sistema." : "Análisis y reparación del almacén de componentes.",
                    DetailedDescription = (scan ? "DISM ScanHealth no intenta reparar. Es más profundo que CheckHealth y puede detectar corrupción aún no registrada." : sfc ? "SFC comprueba todos los archivos protegidos e intenta reparar automáticamente los dañados. Realiza cambios reales. Microsoft recomienda DISM RestoreHealth antes de SFC al solucionar corrupción." : "DISM RestoreHealth analiza y repara automáticamente la corrupción corregible. Realiza cambios reales y normalmente utiliza Windows Update para obtener componentes necesarios. No se proporciona /Source ni /LimitAccess. No reinicia automáticamente.") + " Requiere administrador y puede tardar varios minutos. Una vez iniciado, se espera a que termine sin forzar cancelación.",
                    Category = TaskCategory.Repair, ImpactLevel = scan ? ImpactLevel.Information : ImpactLevel.Repair, TaskType = TaskType.Command,
                    Command = sfc ? SystemPath("sfc.exe") : DismPath, Arguments = sfc ? "/scannow" : scan ? "/Online /Cleanup-Image /ScanHealth /English" : "/Online /Cleanup-Image /RestoreHealth /English",
                    RequiresElevation = true, CanBeCancelled = false, MayRequireRestart = !scan };
            }
            return new MaintenanceTask { Id = CheckHealthId, Name = "Comprobar integridad de Windows",
                ShortDescription = "Comprueba rápidamente si Windows ya tiene registrada corrupción en el almacén de componentes.",
                DetailedDescription = "DISM CheckHealth consulta el estado registrado del almacén de componentes; no intenta reparar ni realiza un análisis exhaustivo. Requiere administrador y normalmente es rápida. Según el resultado puede recomendar ScanHealth o RestoreHealth, que nunca se ejecutarán automáticamente. /English solo hace consistente la salida; no cambia el idioma de Windows. Una vez iniciado, se espera a que termine sin forzar su cancelación.",
                Category = TaskCategory.Repair, ImpactLevel = ImpactLevel.Information, TaskType = TaskType.Command,
                Command = DismPath, Arguments = CheckHealthArguments, RequiresElevation = true, CanBeCancelled = false, MayRequireRestart = false };
        }
        public static string DismPath => SystemPath("DISM.exe");
        private static string SystemPath(string name) => SystemPathFromWindowsDirectory(Environment.GetFolderPath(Environment.SpecialFolder.Windows), name,
            Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess);
        internal static string SystemPathFromWindowsDirectory(string windows, string name, bool useSysnative)
        {
            WindowsVolumeResolver.FromWindowsPath(windows); // Fail closed: never resolve a missing Windows directory relative to the worker's folder.
            if (name != "DISM.exe" && name != "sfc.exe" && name != "chkdsk.exe" && name != "netsh.exe" && name != "ipconfig.exe")
                throw new ArgumentException("Ejecutable del sistema no permitido.");
            return Path.Combine(windows, useSysnative ? "Sysnative" : "System32", name);
        }
    }
}
