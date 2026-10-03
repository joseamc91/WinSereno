# Changelog

## Unreleased

## 0.1.0-beta.4 — 2026-10-03

- Navegación lateral rediseñada con iconos lineales, Ajustes separado y enlaces directos a GitHub.
- Mejorada la jerarquía visual de botones principales, especialmente en modo oscuro, y unificados sus tamaños de texto.
- Ajustes reorganizado con tema Claro/Oscuro/Sistema, acceso a carpetas, información de la aplicación y restablecimiento de preferencias.
- Simplificadas las cabeceras de Ajustes y Actividad.

## 0.1.0-beta.3 — 2026-10-02

- Restablecer TCP/IP detecta configuraciones IPv4 manuales y muestra su información con una segunda confirmación de seguridad antes de ejecutar.
- Detalles del adaptador queda alineado en la parte inferior de las tarjetas de Red.
- Limpieza simplificada con una única acción de análisis, controles más claros y resultados compactos por categoría.
- La versión pública visible de WinSereno se separa de la versión técnica del ensamblado para identificar correctamente builds de desarrollo y releases.

## 0.1.0-beta.2 — 2026-10-02

- Corregida la decodificación de la salida de netsh para interpretar correctamente resultados localizados de Winsock, TCP/IP y reinicio de adaptador, preservando los avisos de reinicio.
- Red rediseñada con tarjetas compactas de adaptadores, grid de tres columnas, una cabecera única con estado de conectividad simplificado y detalles sin información duplicada.
- Inicio y Red comparten ahora una disposición de tres columnas que aprovecha mejor el ancho disponible.

## 0.1.0-beta.1 — 2026-10-02

- Diagnóstico ahora incluye la comprobación de integridad de Windows con un único UAC, utilizando DISM CheckHealth y SFC VerifyOnly sin realizar reparaciones.
- Diagnóstico se ha simplificado y compactado, con explicaciones previas, resultados más claros y estados alineados.
- El panel global de operaciones se ha sustituido por un toast flotante que conserva progreso, detalles e historial sin ocupar espacio en las páginas.
- Reparación presenta ahora una cabecera simplificada y herramientas individuales más compactas, con nombres descriptivos y sus términos técnicos.
- Inicio muestra información más completa y compacta de CPU, GPU, RAM, red y discos.
- Se elimina el indicador global de reinicio pendiente de Inicio y Diagnóstico; se mantienen los avisos de reinicio de operaciones concretas.
- Registro de acciones de la sesión y cierre de resultados terminados del toast global sin perder sus detalles.
- Aplicación portable para Windows con los módulos Inicio, Diagnóstico, Reparación, Red, Limpieza, Ajustes y Actividad.
- Información real del equipo y diagnóstico general de solo lectura; Integridad solicita un único UAC, sin elevar la aplicación principal.
- Herramientas DISM CheckHealth, ScanHealth y RestoreHealth; SFC; CHKDSK de solo lectura y Component Cleanup como mantenimiento.
- Reparación completa con ScanHealth, RestoreHealth condicional y SFC, con una confirmación y un único UAC.
- Información de adaptadores y conectividad, vaciado de caché DNS, renovación DHCP, reinicio de adaptador y restablecimientos Winsock/TCP/IP, con advertencias para sesiones RDP.
- Análisis y limpieza de temporales del usuario, Windows Temp, miniaturas y Papelera, individualmente o por categorías, con estimaciones recuperables y reanálisis posterior.
- Limpieza que omite archivos protegidos o inaccesibles y evita seguir reparse points, sin cambiar permisos ni tomar propiedad.
- Último análisis elevado de Windows Temp identificado con su fecha y hora reales.
- Tema claro/oscuro persistente y pantallas más compactas, con Reparación completa destacada y diagnóstico inicial simplificado.
- Logs detallados por sesión junto al ejecutable y apertura de su carpeta desde Ajustes.
- Validación del almacenamiento portable escribible, sin ubicaciones alternativas ni elevación automática.
- Selección del resultado DISM de integridad más reciente según su finalización, manteniendo SFC separado.
- Corrección del streaming de SFC mediante reconstrucción de líneas y reconocimiento del progreso real en español e inglés.
- Resolución interna del volumen de Windows para CHKDSK, corrección de su decodificación y reconocimiento de problemas detectados como atención.
- Aplicación principal asInvoker y worker elevado en el mismo ejecutable, con TaskIds permitidos, argumentos internos y revalidación de datos mutables.
- Comunicación local autenticada mediante named pipe, nonce aleatorio, ACL, validación de PID y handshake con timeout.
- Una sola operación activa y bloqueo de cierre durante tareas no cancelables.
- Solución autónoma, pruebas y fixtures dentro de Tests, y documentación pública inicial.
