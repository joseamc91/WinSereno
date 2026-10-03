# WinSereno

Aplicación portable para Windows centrada en diagnóstico, mantenimiento y reparación mediante herramientas nativas del sistema. Ofrece acciones explícitas y resultados visibles, sin filosofía de «PC optimizer».

## Estado

WinSereno está en fase **Beta / pre-release**. **v0.1.0-beta.4** es la beta pública actual; no es una versión estable.

La versión pública visible sigue los tags y pre-releases. AssemblyVersion y FileVersion permanecen actualmente en `0.1.0.0`.

Las funciones implementadas y su validación son aspectos distintos: algunas cuentan con pruebas manuales y otras requieren más validación en equipos reales.

## Características

| Módulo | Funcionalidad actual |
|---|---|
| Inicio | Información real de Windows, CPU, GPU integrada/dedicada cuando se identifican, RAM y detalles de módulos cuando Windows los expone, red principal, uptime, discos locales y unidades removibles en secciones separadas. Tarjetas principales en tres columnas y actualización manual. |
| Diagnóstico | Seis comprobaciones: espacio de almacenamiento, salud básica de almacenamiento, red local e Internet, servicios críticos, eventos de Windows e integridad de Windows. Las cinco primeras utilizan permisos normales; Integridad ejecuta DISM CheckHealth y SFC VerifyOnly con un único UAC. Ninguna realiza reparaciones. |
| Reparación | DISM CheckHealth, ScanHealth y RestoreHealth; SFC /scannow; CHKDSK de solo lectura; StartComponentCleanup y Reparación completa condicional. |
| Red | Tarjetas compactas de adaptadores físicos en tres columnas, estado de conectividad simplificado y detalles separados por adaptador y pruebas globales. Caché DNS, DHCP, reinicio de adaptador y restablecimientos Winsock/TCP/IP. |
| Limpieza | Una única acción de análisis para temporales, miniaturas y Papelera, con Windows Temp integrado mediante UAC. Limpieza individual o de categorías marcadas únicamente tras un análisis válido, con resultados compactos. |
| Ajustes | Tema Claro/Oscuro/Sistema persistente, acceso a las carpetas de logs y WinSereno, información de la aplicación, enlaces a GitHub/Releases y restablecimiento de preferencias visuales. La búsqueda de actualizaciones permanece deshabilitada. |
| Actividad | Historial de acciones de la sesión con resumen, duración y detalles. No se conserva al cerrar; los TXT de Logs siguen siendo el registro persistente. |

Al pulsar **Analizar este PC**, se realizan las comprobaciones generales y se solicita un único UAC para Integridad. El mismo worker elevado ejecuta DISM CheckHealth y SFC VerifyOnly, sin reparar. Si se cancelan los permisos, las demás comprobaciones continúan e Integridad queda **No comprobado**. Un dato no disponible no se interpreta como un problema detectado.

Las operaciones que modifican el sistema requieren confirmación. La aplicación principal arranca con permisos normales; UAC se solicita únicamente cuando corresponde a la acción iniciada por el usuario. Solo puede existir una operación activa.

WinSereno nunca reinicia ni apaga Windows automáticamente. No incluye limpiador de registro, optimizador de RAM ni tweaks agresivos.

### Reparación

- **DISM CheckHealth:** comprobación rápida del estado registrado del almacén de componentes.
- **DISM ScanHealth:** análisis profundo sin reparación.
- **DISM RestoreHealth:** análisis y reparación del almacén de componentes.
- **SFC /scannow:** comprobación y reparación de archivos protegidos del sistema.
- **CHKDSK:** comprobación de solo lectura del volumen de la instalación actual de Windows, resuelto internamente. No repara ni programa reparaciones.
- **Component Cleanup / StartComponentCleanup:** mantenimiento que elimina versiones reemplazadas de componentes. No es una reparación de corrupción.
- **Reparación completa:** ejecuta ScanHealth; omite RestoreHealth si el almacén está sano, o lo ejecuta si la corrupción es reparable; continúa con SFC solo si los pasos anteriores lo permiten. Utiliza una confirmación y un único UAC. Los pasos omitidos y sus motivos quedan visibles.

Un toast flotante global muestra la operación activa, el tiempo, el progreso real cuando existe y el resumen final. **Ver detalles** permite consultar la salida completa y el resultado. Al finalizar puede cerrarse sin eliminar el resultado de la sesión, Actividad ni Logs. No se inventan porcentajes globales; CHKDSK conserva su progreso nativo en la salida.

### Red

Muestra adaptadores físicos relevantes en tarjetas compactas de tres columnas, con tipo Ethernet/Wi-Fi, nombre, estado, velocidad e IPv4. Gateway, DNS y datos adicionales de Wi-Fi quedan en **Detalles del adaptador**, cuando están disponibles. Inicio y Red comparten la misma disposición de tarjetas para aprovechar el ancho útil.

La cabecera reúne **Estado de red**, un estado breve como **Internet disponible** o **Conexión con incidencias**, y **Actualizar**; la hora de la última consulta aparece debajo. **Detalles de conectividad** conserva las pruebas globales de gateway, ICMP público, DNS y HTTPS, sin repetir la configuración de cada adaptador. La falta de respuesta ICMP por sí sola no demuestra que Internet no funcione.

Herramientas disponibles:

- Vaciar caché DNS.
- Renovar DHCP únicamente cuando existe una interfaz física activa apta y configurada por DHCP.
- Reiniciar un adaptador físico activo seleccionado.
- Restablecer Winsock.
- Restablecer TCP/IP.

Las acciones son independientes y requieren confirmación; UAC se solicita cuando corresponde. Entrar en Red solo consulta información: ninguna herramienta modificadora se ejecuta automáticamente. Las operaciones potencialmente disruptivas incorporan advertencias especiales cuando se detecta una sesión RDP. Si es necesario reiniciar Windows, se informa sin hacerlo automáticamente.

**Restablecer TCP/IP** realiza un preflight de configuración IPv4. Cuando detecta configuración manual, muestra IP, máscara/prefijo, gateway y DNS y exige una segunda confirmación explícita antes de solicitar UAC. Una configuración indeterminada también requiere advertencia y segunda confirmación. WinSereno no guarda ni restaura automáticamente esos datos; el restablecimiento sigue siendo una acción administrativa explícita.

Se han validado manualmente la lectura y actualización de Red, el reinicio de un adaptador Ethernet y el restablecimiento Winsock. DHCP se detiene de forma segura cuando no existen interfaces elegibles. También se ha validado manualmente el flujo preventivo de TCP/IP con una IPv4 estática real, sin ejecutar el restablecimiento. El comando TCP/IP permanece sin validación manual real por su posible impacto sobre la configuración de red. Estas comprobaciones no equivalen a una validación exhaustiva en todos los equipos.

### Limpieza

Categorías disponibles:

- Temporales del usuario.
- Temporales de Windows.
- Caché de miniaturas del usuario.
- Papelera de reciclaje.

El análisis previo muestra de forma compacta el espacio encontrado y recuperable estimado; no borra archivos ni inicia limpiezas automáticamente. La estimación no garantiza el espacio finalmente liberado. La política de borrado es conservadora: los archivos protegidos, en uso o no elegibles se omiten, y los resultados parciales se distinguen de una limpieza completa.

Se puede limpiar cada categoría o usar **Limpiar categorías marcadas**. Las tres primeras están seleccionadas inicialmente; Papelera no. Vaciar la Papelera elimina la posibilidad de restaurar sus elementos desde ella y requiere una confirmación explícita.

La acción **Analizar** consulta las cuatro categorías y solicita un único UAC para integrar Windows Temp. Si se cancela, las categorías normales conservan sus resultados y Windows Temp queda sin comprobar. Las acciones de borrado requieren un análisis válido de su categoría. Su último snapshot elevado conserva la hora del análisis durante la sesión. La limpieza de Windows Temp requiere elevación y reanaliza dentro del mismo worker. La limpieza seleccionada solicita un único UAC antes de borrar si incluye esta categoría. Al terminar se actualizan las categorías afectadas.

## Portabilidad y logs

La navegación lateral conserva icono y nombre para cada módulo; Ajustes se separa en la zona inferior junto a la versión pública y el enlace a GitHub. En **Apariencia**, el tema **Sistema** consulta la preferencia de Windows al iniciar o seleccionarlo; no sigue sus cambios en tiempo real. **Restablecer preferencias** requiere confirmación, aplica el tema Claro por defecto y guarda la configuración sin borrar logs ni modificar Windows.

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
| [HomeInformationChecks.ps1](Tests/HomeInformationChecks.ps1) | Datos y presentación de Inicio: CPU, GPU, RAM, red y discos. |
| [NetworkOutputChecks.ps1](Tests/NetworkOutputChecks.ps1) | Bytes UTF-8/legacy, parsers Winsock y TCP/IP ES/EN, logs y regresiones de red simuladas. |
| [NetworkPresentationChecks.ps1](Tests/NetworkPresentationChecks.ps1) | Cabecera de Red, tarjetas compartidas de tres columnas, 1–6 adaptadores y ambos temas. |
| [NetworkStaticChecks.ps1](Tests/NetworkStaticChecks.ps1) | Estructura, bindings y captura de salida de Red, sin cargar el EXE. |
| [DiagnosticIntegrityChecks.ps1](Tests/DiagnosticIntegrityChecks.ps1) | Integridad de solo lectura, VerifyOnly, combinación de resultados y UAC simulado. |
| [ActivityHistoryChecks.ps1](Tests/ActivityHistoryChecks.ps1) | Historial de sesión, snapshots y cierre de resultados. |
| [ToastRepairPresentationChecks.ps1](Tests/ToastRepairPresentationChecks.ps1) | Toast y herramientas de Reparación en ambos temas, con operaciones simuladas. |
| [ToastRepairStaticChecks.ps1](Tests/ToastRepairStaticChecks.ps1) | Estructura XAML, recursos, foco y bindings del toast y Reparación, sin cargar el EXE. |
| [IntegritySessionChecks.ps1](Tests/IntegritySessionChecks.ps1) | Selección temporal de integridad DISM, cancelaciones y separación de SFC. |
| [SfcStreamingChecks.ps1](Tests/SfcStreamingChecks.ps1) | Reconstrucción de líneas, salida íntegra y progreso SFC español/inglés. |
| [WindowsTempSnapshotChecks.ps1](Tests/WindowsTempSnapshotChecks.ps1) | Snapshot elevado, timestamp y reanálisis con proveedores controlados. |
| [CleanupExperienceChecks.ps1](Tests/CleanupExperienceChecks.ps1) | Análisis unificado, habilitación de acciones, UAC simulado e historial único. |
| [CleanupStaticChecks.ps1](Tests/CleanupStaticChecks.ps1) | Presentación, análisis de solo lectura y versión pública, sin cargar el EXE. |
| [CleanupXamlPresentationChecks.ps1](Tests/CleanupXamlPresentationChecks.ps1) | XAML fuente con datos simulados en claro/oscuro, antes y después del análisis. |
| [TcpIpResetSafetyChecks.ps1](Tests/TcpIpResetSafetyChecks.ps1) | Preflight, configuración manual/indeterminada y doble confirmación simulada. |
| [TcpIpResetStaticChecks.ps1](Tests/TcpIpResetStaticChecks.ps1) | Protecciones, comando fijo y advertencia TCP/IP, sin ejecutar herramientas. |
| [ThemePersistenceChecks.ps1](Tests/ThemePersistenceChecks.ps1) | Configuración portable y persistencia del tema. |
| [Beta4ExperienceChecks.ps1](Tests/Beta4ExperienceChecks.ps1) | Navegación, iconos, botones, temas, preferencias y enlaces con datos simulados, incluidos anchos reducidos. |
| [Beta4StaticChecks.ps1](Tests/Beta4StaticChecks.ps1) | Recursos vectoriales, cabeceras, enlaces fijos y configuración, sin cargar el EXE. |
| [DiagnosticPresentationChecks.ps1](Tests/DiagnosticPresentationChecks.ps1) | Presentación inicial compacta y conservación de resultados reales en ambos temas. |
| [ChkdskRegressionChecks.ps1](Tests/ChkdskRegressionChecks.ps1) | Parser ES/EN, decodificación, streaming, IPC y logs simulados de CHKDSK. |

Los fixtures se conservan en [Tests/Fixtures/](Tests/Fixtures/). Las suites no lanzan herramientas de mantenimiento ni UAC reales; algunas crean y eliminan directorios controlados de prueba.

Tras compilar Debug, pueden ejecutarse individualmente con Windows PowerShell 5.1 en modo STA, por ejemplo:

```powershell
powershell.exe -NoProfile -STA -File .\Tests\ThemePersistenceChecks.ps1
```

Las suites disponibles no equivalen a una validación completa de la beta. En el cierre de Beta 4 se reprodujeron las comprobaciones estáticas, de presentación WPF y de regresión con datos simulados. Las suites relevantes para este bloque se ejecutaron sin bloqueos de Smart App Control. Beta 4 se publicó con la regresión histórica de CHKDSK en español («Acceso denegado»); el desarrollo posterior ya corrige el reconocimiento de mensajes terminados en puntuación y contiene regresiones ES/EN. Esta corrección está bajo Unreleased y se incluirá en la próxima versión publicada; no forma parte del ejecutable de Beta 4. Los bloqueos de harnesses registrados durante Beta 3 son evidencia histórica, no ejecuciones nuevas superadas.

Existen validaciones manuales previas de Inicio, Diagnóstico y DISM CheckHealth/ScanHealth/RestoreHealth. SFC y CHKDSK también han aportado salidas reales para identificar correcciones. Esto no valida todas las acciones ni sustituye las pruebas pendientes de los cambios más recientes. Las operaciones disruptivas de red y varias limpiezas necesitan validación manual controlada adicional.

## Limitaciones conocidas

- Smart App Control puede bloquear builds locales no firmadas o desconocidas. El EXE de esta beta no está firmado con Authenticode. WinSereno no modifica ni desactiva Smart App Control, Defender ni otras políticas de seguridad.
- Se requiere .NET Framework 4.8 y una carpeta portable local escribible.
- Algunas operaciones requieren permisos de administrador mediante UAC.
- Algunas operaciones disruptivas de Red todavía requieren más validación manual controlada.
- Determinadas tareas no son cancelables una vez iniciado el comando nativo; la ventana impide cerrar mientras están activas.
- Una herramienta nativa que no termine puede mantener ocupado el coordinador de operaciones.
- La disponibilidad de datos depende de Windows, hardware, controladores y permisos; un resultado desconocido no se interpreta como saludable.
- No existe aún un sistema integrado de actualización.

La arquitectura y sus límites se describen en [AUDIT_PRIVILEGED_EXECUTION.md](AUDIT_PRIVILEGED_EXECUTION.md). El historial de cambios está en [CHANGELOG.md](CHANGELOG.md) y [CHANGELOG.en.md](CHANGELOG.en.md).

## Licencia

La licencia está pendiente de definir.
