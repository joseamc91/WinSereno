# WinSereno

Aplicación portable para Windows centrada en diagnóstico, mantenimiento y reparación mediante herramientas nativas del sistema. Ofrece acciones explícitas y resultados visibles, sin filosofía de «PC optimizer».

## Estado

WinSereno está en desarrollo, en estado **pre-release**. Todavía no se presenta como una versión estable y no hay una release publicada.

Las funciones implementadas y su validación son aspectos distintos: algunas cuentan con pruebas manuales y otras requieren más validación en equipos reales.

## Características

| Módulo | Funcionalidad actual |
|---|---|
| Inicio | Información real de Windows, CPU, RAM instalada, discos locales, adaptador principal, uptime y reinicio pendiente. Actualización manual. |
| Diagnóstico | Comprobaciones de almacenamiento, reinicio, red, servicios esenciales, plan de energía y eventos conocidos. Solo lectura, sin UAC. Reutiliza la integridad administrativa obtenida explícitamente durante la sesión. |
| Reparación | Herramientas individuales DISM, SFC, CHKDSK de solo lectura, mantenimiento del almacén de componentes y Reparación completa condicional. |
| Red | Información de adaptadores y conectividad, caché DNS, DHCP, reinicio de adaptador y restablecimientos Winsock/TCP/IP. |
| Limpieza | Análisis y limpieza individual o por selección de temporales, miniaturas y Papelera, con estimaciones y resultados por categoría. |
| Ajustes | Tema claro/oscuro persistente y apertura de la carpeta de logs. La búsqueda de actualizaciones permanece deshabilitada. |

El diagnóstico general se inicia al pulsar **Analizar este PC**. No ejecuta herramientas administrativas ni repara automáticamente. Un dato no disponible se presenta como **No comprobado**, no como un problema detectado.

Las operaciones que modifican el sistema requieren confirmación. La aplicación principal arranca con permisos normales; UAC se solicita únicamente cuando corresponde a la acción confirmada. Solo puede existir una operación activa.

WinSereno nunca reinicia ni apaga Windows automáticamente. No incluye limpiador de registro, optimizador de RAM ni tweaks agresivos.

### Reparación

- **DISM CheckHealth:** comprobación rápida del estado registrado del almacén de componentes.
- **DISM ScanHealth:** análisis profundo sin reparación.
- **DISM RestoreHealth:** análisis y reparación del almacén de componentes.
- **SFC:** comprobación y reparación de archivos protegidos del sistema.
- **CHKDSK:** comprobación de solo lectura del volumen de la instalación actual de Windows, resuelto internamente. No repara ni programa reparaciones.
- **Component Cleanup:** mantenimiento que elimina versiones reemplazadas de componentes. No es una reparación de corrupción.
- **Reparación completa:** ejecuta ScanHealth; omite RestoreHealth si el almacén está sano, o lo ejecuta si la corrupción es reparable; continúa con SFC solo si los pasos anteriores lo permiten. Utiliza una confirmación y un único UAC. Los pasos omitidos y sus motivos quedan visibles.

El panel de ejecución permite consultar stdout/stderr, duración y resultado. Los porcentajes se usan cuando proceden de la herramienta, sin estimaciones globales ficticias. CHKDSK conserva su progreso nativo en la salida.

### Red

Muestra adaptadores físicos relevantes, Ethernet/Wi-Fi, estado, velocidad, IPv4, gateway, DNS y resumen de conectividad. Los datos adicionales de Wi-Fi y la capacidad máxima de Ethernet se muestran solo cuando pueden obtenerse de forma fiable.

Herramientas disponibles:

- Vaciar caché DNS.
- Renovar DHCP únicamente cuando existe una interfaz física activa apta y configurada por DHCP.
- Reiniciar un adaptador físico activo seleccionado.
- Restablecer Winsock.
- Restablecer TCP/IP.

Las acciones son independientes y requieren confirmación. Las operaciones potencialmente disruptivas incorporan advertencias especiales cuando se detecta una sesión RDP. Si es necesario reiniciar Windows, se informa sin hacerlo automáticamente.

### Limpieza

Categorías disponibles:

- Temporales del usuario.
- Temporales de Windows.
- Caché de miniaturas del usuario.
- Papelera de reciclaje.

El análisis previo muestra tamaño, cantidad de elementos y espacio recuperable estimado; la estimación no garantiza el espacio finalmente liberado. Los archivos protegidos, en uso o no eliminables se omiten, y los resultados parciales se distinguen de una limpieza completa.

Se puede limpiar cada categoría o usar **Limpiar seleccionados**. Las tres primeras están seleccionadas inicialmente; Papelera no. Vaciar la Papelera elimina la posibilidad de restaurar sus elementos desde ella y requiere una confirmación explícita.

El análisis completo de Windows Temp puede solicitarse con permisos de administrador. Su último snapshot elevado conserva la hora del análisis durante la sesión. La limpieza de Windows Temp requiere elevación y reanaliza dentro del mismo worker. La limpieza seleccionada solicita un único UAC antes de borrar si incluye esta categoría. Al terminar se actualizan las categorías afectadas.

## Portabilidad y logs

La configuración y los logs propios se guardan junto al ejecutable:

```text
WinSereno.exe
config.json
Logs/
```

La carpeta debe ser local y escribible. El inicio comprueba el almacenamiento; no hay fallback a AppData, LocalAppData, ProgramData, Temp ni al registro. No se eleva toda la aplicación para resolver permisos de escritura.

Se crea un log por sesión con resultados y salida detallada de las herramientas. Los resultados funcionales de diagnóstico y reparación permanecen en memoria durante la sesión; los logs se conservan. Antes de compartirlos conviene revisar su contenido.

## Requisitos

- Windows 10 o Windows 11.
- .NET Framework 4.8.
- Aplicación WPF en C#, compilada como **Any CPU**, con **Prefer32Bit=false**.
- Carpeta portable local con permisos de escritura.

No se afirma compatibilidad validada con ARM64. El proyecto no utiliza paquetes NuGet ni frameworks MVVM externos.

## Compilación

Para desarrollar se necesitan MSBuild, herramientas de C#/WPF y el Developer Pack o targeting pack de .NET Framework 4.8.

Abrir `WinSereno.sln`, que referencia `WinSereno.csproj` en la misma carpeta. Desde una consola de desarrollo de Visual Studio:

```text
MSBuild WinSereno.sln /t:Clean,Build /p:Configuration=Debug /p:Platform="Any CPU"
```

La salida Debug se genera en `bin/Debug/`. La aplicación principal conserva el manifest `asInvoker`; las tareas administrativas reutilizan ese mismo ejecutable en un modo worker interno.

## Pruebas

Las suites automatizadas disponibles están en [Tests/](Tests/):

| Suite | Alcance |
|---|---|
| [IntegritySessionChecks.ps1](Tests/IntegritySessionChecks.ps1) | Selección temporal de integridad DISM, cancelaciones y separación de SFC. |
| [SfcStreamingChecks.ps1](Tests/SfcStreamingChecks.ps1) | Reconstrucción de líneas, salida íntegra y progreso SFC español/inglés. |
| [WindowsTempSnapshotChecks.ps1](Tests/WindowsTempSnapshotChecks.ps1) | Snapshot elevado, timestamp y reanálisis con proveedores controlados. |
| [ThemePersistenceChecks.ps1](Tests/ThemePersistenceChecks.ps1) | Configuración portable y persistencia del tema. |
| [DiagnosticPresentationChecks.ps1](Tests/DiagnosticPresentationChecks.ps1) | Presentación inicial compacta y conservación de resultados reales en ambos temas. |
| [ChkdskRegressionChecks.ps1](Tests/ChkdskRegressionChecks.ps1) | Parser ES/EN, decodificación, streaming, IPC y logs simulados de CHKDSK. |

Los fixtures se conservan en [Tests/Fixtures/](Tests/Fixtures/). Las suites no lanzan herramientas de mantenimiento ni UAC reales; algunas crean y eliminan directorios controlados de prueba.

Tras compilar Debug, pueden ejecutarse individualmente con Windows PowerShell 5.1 en modo STA, por ejemplo:

```powershell
powershell.exe -NoProfile -STA -File .\Tests\ThemePersistenceChecks.ps1
```

La suite de regresión CHKDSK ha compilado, pero su ejecución y la repetición de las suites de integridad y SFC sobre el último binario quedaron bloqueadas por App Control. No se consideran superadas esas ejecuciones bloqueadas.

Existen validaciones manuales previas de Inicio, Diagnóstico y DISM CheckHealth/ScanHealth/RestoreHealth. SFC y CHKDSK también han aportado salidas reales para identificar correcciones. Esto no valida todas las acciones ni sustituye las pruebas pendientes de los cambios más recientes. Las operaciones disruptivas de red y varias limpiezas necesitan validación manual controlada adicional.

## Limitaciones conocidas

- Smart App Control puede bloquear builds locales sin firma o desconocidas. WinSereno no cambia las políticas de seguridad.
- Algunas operaciones requieren elevación explícita.
- Determinadas tareas no son cancelables una vez iniciado el comando nativo; la ventana impide cerrar mientras están activas.
- Una herramienta nativa que no termine puede mantener ocupado el coordinador de operaciones.
- La disponibilidad de datos depende de Windows, hardware, controladores y permisos; un resultado desconocido no se interpreta como saludable.
- No existe aún un sistema integrado de actualización.

La arquitectura y sus límites se describen en [AUDIT_PRIVILEGED_EXECUTION.md](AUDIT_PRIVILEGED_EXECUTION.md). Los cambios pendientes de publicación están en [CHANGELOG.md](CHANGELOG.md).

## Licencia

La licencia está pendiente de definir.
