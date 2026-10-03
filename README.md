<p align="center">
  <img src="Assets/Brand/Banner.png" alt="WinSereno" width="100%">
</p>

<p align="center">
  <strong>Español</strong> · <a href="README.en.md">English</a>
</p>

<p align="center">
  <a href="https://github.com/joseamc91/WinSereno/actions/workflows/ci.yml">
    <img src="https://github.com/joseamc91/WinSereno/actions/workflows/ci.yml/badge.svg" alt="CI">
  </a>
  <a href="https://github.com/joseamc91/WinSereno/releases/latest">
    <img src="https://img.shields.io/github/v/release/joseamc91/WinSereno?label=Stable&color=1F4E6B" alt="Última versión estable">
  </a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 y 11">
  <a href="LICENSE">
    <img src="https://img.shields.io/badge/Licencia-GPLv3-58B7B1" alt="Licencia GNU GPLv3">
  </a>
</p>

<p align="center">
  WinSereno reúne herramientas de diagnóstico, mantenimiento y reparación de Windows en una interfaz clara y portable. Tú eliges las acciones y puedes consultar sus resultados.
</p>

<p align="center">
  <a href="https://github.com/joseamc91/WinSereno/releases/latest">
    <img src="https://img.shields.io/badge/DESCARGAR%20WINSERENO-1F4E6B?style=for-the-badge" alt="DESCARGAR WINSERENO">
  </a>
</p>

<p align="center">
  <strong>Portable · Sin instalación · Herramientas nativas de Windows</strong>
</p>

<p align="center">
  <img src="docs/assets/Screenshot_Themes.png" alt="Inicio de WinSereno con temas Claro y Oscuro" width="100%">
  <br>
  <sub>Interfaz actual en español. Presentación de los temas con datos de ejemplo.</sub>
</p>

## Qué es WinSereno

WinSereno ofrece un lugar común para consultar el estado del equipo y utilizar herramientas de Windows como DISM, SFC y CHKDSK. Prioriza el diagnóstico y las acciones explícitas, con confirmación antes de realizar cambios y resultados que puedes revisar.

Su enfoque es el mantenimiento con control, sin promesas de aceleración ni ajustes opacos de un «PC optimizer». No reinicia ni apaga Windows automáticamente.

## Funciones

| Área | Qué puedes hacer |
| --- | --- |
| Inicio | Consultar Windows, CPU, GPU, RAM, red y tiempo de actividad. Ver discos locales y unidades externas que Windows identifica como removibles, en secciones separadas. |
| Diagnóstico | Comprobar espacio y salud básica del almacenamiento, red e Internet, servicios críticos, eventos e integridad de Windows mediante DISM CheckHealth y SFC VerifyOnly. **No aplica reparaciones.** |
| Reparación | Ejecutar DISM CheckHealth, ScanHealth y RestoreHealth; SFC; limpieza de componentes y una secuencia de reparación completa condicional. Incluye CHKDSK de solo lectura, sin reparar ni programar reparaciones. |
| Red | Consultar adaptadores, vaciar la caché DNS, renovar DHCP, reiniciar un adaptador y restablecer Winsock o TCP/IP. El reset TCP/IP comprueba IPv4 y exige una segunda confirmación si la configuración es manual o indeterminada. |
| Limpieza | Analizar primero temporales del usuario y de Windows, caché de miniaturas y Papelera. Limpiar categorías con análisis válido mediante una política conservadora; la Papelera permanece desmarcada por defecto. |
| Actividad | Revisar las acciones de la sesión, sus resultados y detalles. Los logs TXT mantienen el registro persistente. |
| Configuración (Ajustes) | Elegir Claro, Oscuro o Sistema; abrir las carpetas de la aplicación y logs; consultar información y enlaces del proyecto, y restablecer preferencias visuales. |

La disponibilidad de algunos datos depende del hardware, los controladores y los permisos. Las estimaciones de limpieza no garantizan el espacio finalmente liberado. El restablecimiento TCP/IP no guarda ni restaura automáticamente la configuración de red.

## Cómo usarlo

1. [Descarga la última versión estable](https://github.com/joseamc91/WinSereno/releases/latest).
2. Coloca `WinSereno.exe` en una carpeta local con permisos de escritura.
3. Ejecuta la aplicación y elige una sección.
4. Revisa la información y las advertencias antes de confirmar una acción.

Algunas operaciones solicitarán permisos de administrador mediante UAC. La aplicación arranca con permisos normales y no ejecuta limpiezas ni reparaciones automáticamente.

## Portable

WinSereno no necesita instalador. La configuración (`config.json`) y los registros (`Logs/`) se guardan junto al ejecutable, en su propia carpeta.

Puedes mover la aplicación conservando toda la carpeta. Antes de compartir logs, revisa su contenido: pueden incluir información del equipo.

## Compatibilidad

- Windows 10 y Windows 11.
- .NET Framework 4.8.
- Carpeta local escribible.

La interfaz de WinSereno 1.0.0 está en español. Este proyecto dispone también de documentación en inglés.

## Seguridad

Las acciones que modifican el sistema requieren confirmación; algunas también requieren UAC. WinSereno no reinicia ni apaga el equipo automáticamente y no desactiva protecciones de Windows.

El ejecutable todavía no dispone de firma digital comercial ni firma Authenticode. En determinados equipos, Smart App Control / Control inteligente de aplicaciones puede bloquearlo al no poder verificar su publicador. No recomendamos desactivar protecciones ni añadir exclusiones para evitar ese bloqueo.

## Roadmap

Planes futuros, sujetos al uso y al feedback:

- Comprobación y actualización desde la propia aplicación.
- Interfaz en inglés.
- Mejoras basadas en la experiencia de los usuarios.

## Documentación

- [Historial de cambios](CHANGELOG.md)
- [Licencia GNU GPLv3](LICENSE)
- [Notas técnicas sobre ejecución administrativa](AUDIT_PRIVILEGED_EXECUTION.md)
