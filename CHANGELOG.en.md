# Changelog

## Unreleased

- Home displays more complete and compact CPU, GPU, RAM, network, and disk information.
- Removed the global pending restart indicator from Home and Diagnostics; restart notices for specific operations are retained.
- Session action history and dismissal of completed results from the global panel without losing their details.
- Portable Windows application with Home, Diagnostics, Repair, Network, Cleanup, and Settings modules.
- Real system information and read-only general diagnostics, without automatic elevation.
- DISM CheckHealth, ScanHealth, and RestoreHealth tools; SFC; read-only CHKDSK; and Component Cleanup for maintenance.
- Complete Repair with ScanHealth, conditional RestoreHealth, and SFC, with one confirmation and a single UAC prompt.
- Adapter and connectivity information, DNS cache flushing, DHCP renewal, adapter restart, and Winsock/TCP/IP resets, with warnings for RDP sessions.
- Analysis and cleanup of user temporary files, Windows Temp, thumbnails, and the Recycle Bin, individually or by category, with estimated recoverable space and subsequent reanalysis.
- Cleanup that skips protected or inaccessible files and avoids following reparse points, without changing permissions or taking ownership.
- The latest elevated Windows Temp analysis identified by its actual date and time.
- Persistent light/dark theme and more compact screens, with Complete Repair highlighted and a simplified initial diagnostics view.
- Detailed per-session logs alongside the executable, with access to their folder from Settings.
- Validation of writable portable storage, without alternative locations or automatic elevation.
- Selection of the most recent DISM integrity result by its completion time, keeping SFC separate.
- Fixed SFC streaming through line reconstruction and recognition of actual progress in Spanish and English.
- Internal resolution of the Windows volume for CHKDSK, corrected output decoding, and classification of detected problems as Attention.
- Main application running asInvoker and an elevated worker within the same executable, with allowlisted TaskIds, internally defined arguments, and revalidation of mutable data.
- Authenticated local communication through a named pipe, random nonce, ACL, PID validation, and a handshake with a timeout.
- A single active operation and prevention of application closure during non-cancelable tasks.
- Standalone solution, tests and fixtures within Tests, and initial public documentation.
