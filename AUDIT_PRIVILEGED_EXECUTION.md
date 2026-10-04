# Superficie de ejecución privilegiada de WinSereno

Este documento describe la arquitectura actual y los límites de la ejecución privilegiada. Es documentación técnica basada en revisión estática; no certifica la seguridad de la aplicación ni implica que todas las operaciones hayan sido validadas manualmente.

## Modelo de elevación

La aplicación principal mantiene el manifest `asInvoker` y funciona como usuario normal. Una acción administrativa confirmada relanza el mismo `WinSereno.exe` mediante `ProcessStartInfo.Verb = "runas"`, en modo interno `--elevated-worker`.

El worker no abre la ventana principal ni relanza permanentemente toda la aplicación como administrador. No existen helpers propios, servicios ni tareas programadas para esta elevación. La ruta del ejecutable se obtiene del proceso actual, sin una ruta de distribución fija.

La entrada del worker admite exclusivamente metadatos posicionales validados: TaskId, identificador del pipe, nonce y PID del padre. No admite ejecutables, argumentos, scripts ni listas de comandos arbitrarios.

## Catálogo y operaciones

`ElevatedTaskCatalog` contiene una allowlist explícita. Las definiciones se reconstruyen internamente en el límite de ejecución; las rutas de herramientas se resuelven desde la instalación de Windows y se validan. Un TaskId desconocido se rechaza.

| TaskId | Operación conocida | Elevación | Validación en worker |
|---|---|---|---|
| repair.dism.checkhealth | DISM CheckHealth, salida en inglés | Sí | Definición fija del catálogo y ruta del sistema. |
| repair.dism.scanhealth | DISM ScanHealth, salida en inglés | Sí | Definición fija del catálogo y ruta del sistema. |
| repair.dism.restorehealth | DISM RestoreHealth, salida en inglés | Sí | Definición fija; sin fuente ni argumentos enviados por la UI. |
| repair.sfc.scannow | SFC con su opción de comprobación y reparación | Sí | Ejecutable y argumentos fijos. |
| repair.complete | ScanHealth → RestoreHealth condicional → SFC | Sí, una vez | Secuencia fija; no acepta listas de tareas o comandos. |
| repair.chkdsk.scan | CHKDSK de solo lectura sobre el volumen de Windows | Sí | Volumen resuelto de nuevo internamente; sin opciones ni unidad enviada por la UI. |
| repair.dism.componentcleanup | DISM StartComponentCleanup, salida en inglés | Sí | Argumentos fijos; no admite opciones adicionales de limpieza. |
| cleanup.windowstemp.analyze | Análisis .NET de temporales de Windows | Sí | Ruta interna validada; solo lectura y sin seguir reparse points. |
| cleanup.windowstemp | Limpieza .NET de temporales de Windows y reanálisis | Sí | Ruta interna, revalidación de objetos y conservación de la raíz. |
| cleanup.selected | Windows Temp y reanálisis dentro de la limpieza seleccionada | Solo si incluye Windows Temp | El worker ejecuta únicamente su operación fija; las categorías del usuario siguen en el padre normal. |
| network.restartadapter | Deshabilitar/habilitar adaptador y recuperación acotada | Sí | Reconstruye selección física; fingerprint y revalidación antes de cada comando. |
| network.resetwinsock | netsh winsock reset | Sí | Ejecutable y argumentos fijos. |
| network.resettcpip | netsh int ip reset | Sí | Ejecutable y argumentos fijos. |
| network.flushdns | ipconfig /flushdns | Solo tras denegación explícita | Reconstruye la misma acción fija confirmada. |
| network.renewdhcp | ipconfig release/renew por interfaz elegible | Solo tras denegación explícita | Reconstruye plan físico/DHCP; valida fingerprint, punto de reanudación y estado. |

`cleanup.usertemp`, `cleanup.thumbnails` y `cleanup.recyclebin` no están admitidos en el worker. Se ejecutan en la instancia normal con rutas o ámbitos resueltos internamente. Tampoco aceptan rutas o patrones arbitrarios de la interfaz.

Las comprobaciones generales de Diagnóstico no invocan el worker. Reutilizan los resultados administrativos de integridad obtenidos explícitamente durante la sesión.

## IPC local y autenticación

La comunicación utiliza `NamedPipeServerStream` y `NamedPipeClientStream`, sin TCP ni puertos de red.

- Cada ejecución crea un nombre de pipe impredecible y un nonce aleatorio adicional de 32 bytes mediante un generador criptográfico.
- La ACL se limita al SID del usuario actual, sin herencia, y deniega acceso mediante NetworkSid.
- La instancia principal comprueba el PID del cliente; el worker comprueba el PID del servidor y la ruta del mismo ejecutable.
- El handshake valida la marca de protocolo y el nonce antes de ejecutar operaciones.
- La conexión y el handshake tienen un timeout de 15 segundos.
- Los mensajes son tipados y los campos de texto tienen límites de longitud.
- El nonce no se escribe en logs. Se utiliza en los parámetros de lanzamiento y en el handshake.

Las solicitudes adicionales de DHCP, reinicio de adaptador y limpieza seleccionada son datos de control limitados; no constituyen una vía para enviar comandos o rutas arbitrarios.

## Ejecución y revalidación

Las herramientas se lanzan directamente, con rutas conocidas, `UseShellExecute=false`, stdout/stderr redirigidos y sin ventana de consola. No se usan wrappers cmd, PowerShell ni scripts temporales.

Los argumentos de red que dependen de una interfaz se construyen desde un inventario interno. El worker vuelve a comprobar los datos mutables y no confía exclusivamente en la selección de la instancia no elevada. La recuperación de adaptador sigue intentando habilitarlo localmente incluso si se pierde IPC.

La limpieza privilegiada no toma propiedad, cambia permisos ni mata procesos. Valida rutas, ancestros, atributos y objetos antes de eliminar; no sigue junctions, symlinks ni reparse points y nunca elimina la carpeta raíz gestionada.

`OperationCoordinator` mantiene una sola operación global. La cancelación de UAC se registra como cancelación y libera la operación. Los fallos de inicio, transporte o protocolo se manejan con mensajes y logging; si hay un worker no cancelable todavía activo, se espera su finalización sin matarlo.

Ninguna acción reinicia o apaga Windows automáticamente. La necesidad de reinicio se comunica como resultado.

## Almacenamiento portable

La configuración y los logs propios se mantienen junto al ejecutable. La validación de almacenamiento exige una base absoluta local, comprueba permisos mediante pruebas de escritura y eliminación y rechaza reparse points en ubicaciones gestionadas.

No hay fallback a otra ubicación ni elevación automática para escribir configuración o logs. El worker no crea una configuración alternativa ni su propio log de sesión; comunica resultados al padre.

## Evidencia y alcance de verificación

Las suites actualmente incluidas residen en [Tests/](Tests/) y se descubren automáticamente en el [workflow de CI](.github/workflows/ci.yml). Cubren componentes concretos con muestras y proveedores controlados; no son una prueba completa de todas las operaciones privilegiadas.

La existencia de una suite o una compilación correcta no implica una ejecución superada. Durante validaciones locales anteriores, App Control bloqueó la carga del binario en la regresión CHKDSK y en las suites de integridad y SFC; esas ejecuciones locales no se contaron como superadas ni se alteraron políticas para completarlas. Las suites automatizadas actuales también se ejecutan en CI; su resultado debe comprobarse para el commit concreto que se quiera validar.

Las validaciones manuales anteriores tampoco constituyen una certificación del binario actual. Esta documentación no reproduce logs, identificadores de políticas ni detalles del equipo de desarrollo.

## Limitaciones técnicas

- No cubre sustitución del EXE portable, inyección de código ni compromiso del usuario o del sistema.
- El nonce en la línea de comandos no es un secreto frente al mismo usuario con acceso al proceso. La autenticación también depende de ACL, PID y ruta del ejecutable.
- No se aplica un timeout de terminación ni cancelación forzada a DISM, SFC o CHKDSK. Una herramienta o worker que nunca termine puede mantener ocupada la operación global.
- La recuperación ante EOF o fallos de transporte espera al worker no cancelable. No garantiza recuperación ante un proceso que permanece bloqueado indefinidamente.
- La elevación con credenciales de otro usuario puede impedir la conexión por la ACL del SID original. Se informa del fallo o timeout; no se amplía la ACL automáticamente.
- Las verificaciones previas no eliminan por sí solas todas las carreras posibles ni acreditan resistencia ante manipulación del propio proceso.
