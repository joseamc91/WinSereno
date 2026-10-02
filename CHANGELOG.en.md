# Changelog

## Unreleased

## 0.1.0-beta.2 — 2026-10-02

- Fixed netsh output decoding to correctly interpret localized Winsock, TCP/IP, and adapter restart results, preserving restart notices.
- Redesigned Network with compact adapter cards, a three-column grid, a single header with simplified connectivity status, and details without duplicated information.
- Home and Network now share a three-column layout that makes better use of the available width.

## 0.1.0-beta.1 — 2026-10-02

- Diagnostics now includes Windows integrity checks with a single UAC prompt, using DISM CheckHealth and SFC VerifyOnly without performing repairs.
- Diagnostics has been simplified and made more compact, with explanations before analysis, clearer results, and aligned statuses.
- The global operation panel has been replaced by a floating toast that retains progress, details, and history without taking up space on the pages.
- Repair now features a simplified header and more compact individual tools, with descriptive names and their technical terms.
- Home displays more complete and compact CPU, GPU, RAM, network, and disk information.
- Removed the global pending restart indicator from Home and Diagnostics; restart notices for specific operations are retained.
- Session action history and dismissal of completed results from the global toast without losing their details.
- Portable Windows application with Home, Diagnostics, Repair, Network, Cleanup, Settings, and Activity modules.
- Real system information and read-only general diagnostics; Integrity requests a single UAC prompt without elevating the main application.
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
