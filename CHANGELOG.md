# Changelog

## Unreleased

- Inicio muestra información más completa y compacta de CPU, GPU, RAM, red y discos.
- Se elimina el indicador global de reinicio pendiente de Inicio y Diagnóstico; se mantienen los avisos de reinicio de operaciones concretas.
- Registro de acciones de la sesión y cierre de resultados terminados del panel global sin perder sus detalles.
- Aplicación portable para Windows con los módulos Inicio, Diagnóstico, Reparación, Red, Limpieza y Ajustes.
- Información real del equipo y diagnóstico general de solo lectura, sin elevación automática.
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
