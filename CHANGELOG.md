# Changelog

All notable changes to KillerMCP are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.4] - 2026-09-28

### Fixed

- Checked for signed updates at Windows sign-in even when the MCP host cannot start.

## [0.3.3] - 2026-09-28

### Fixed

- Removed the extra ASP.NET Core runtime requirement from the Windows host.
- Registered Claude Desktop through the configuration file used by Windows packaged installations.

## [0.3.2] - 2026-09-28

### Fixed

- Showed the KillerMCP name in Claude Desktop and clarified setup and restart guidance.
- Removed the misleading topology diagram from KillerScan PDF reports.

## [0.3.1] - 2026-09-28

### Fixed

- Matched the Windows installer and setup dialogs to black surfaces with fuchsia accents.

## [0.3.0] - 2026-09-28

### Added

- Added Killendar appointment creation through its open, unlocked calendar.
- Added a Black/Fuchsia Windows updater with a large KillerMCP icon and controls to disable checks, check and ask, download and ask, or install automatically.
- Added KillerNotes tools for reading, writing, organizing, coloring, image import, history, links, statistics, and file export through the open app.
- Added cross app workflows for PDF creation, note export, network reports, agenda reports, directory reports, and PDF page image notes.

### Fixed

- Update checks now refresh hourly instead of keeping a stale release result for a full day.

## [0.2.1] - 2026-09-27

0.2.1 expands KillerShell file and Windows inspection through the shared MCP connection.

### Added

- Added read-only KillerShell tools for file browsing, text reading, file hashing, processes, services, event logs, registry keys, and drives.

## [0.2.0] - 2026-09-27

0.2.0 replaces the bundled Node server with a smaller native .NET 10 host.

### Added

- Added the native KillerTools engine, daily update checks, and complete KillerScan command coverage.

### Changed

- Rebuilt the host, client registration, installer, repair, upgrade, and uninstall paths for native .NET 10.

## [0.1.1] - 2026-09-26

0.1.1 expands KillerPDF automation and makes local app discovery safer.

### Added

- Added KillerPDF tools for extraction, splitting, decryption, rendering, flattening, printing, OCR, resaving, render benchmarking, page editing, document details, and text search.

### Fixed

- Prevented incompatible older KillerNotes, Killendar, and KillerShell installations from opening during tool discovery.

## [0.1.0] - 2026-09-26

0.1.0 introduces one local MCP connection for KillerTools and the Killer app family.

### Added

- Added all 81 KillerTools website utilities through one installed local connection.
- Added automatic tool discovery for KillerPDF, KillerScan, KillerNotes, KillerShell, and Killendar.
- Added registration for Codex, Claude Code, Claude Desktop, Cursor, GitHub Copilot, Gemini CLI, and Windsurf.
- Added the signed Windows installer and Installed Apps uninstall entry.
