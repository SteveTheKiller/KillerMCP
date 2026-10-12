# Changelog

All notable changes to KillerMCP are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.4.1] - Unreleased

### Changed

- Capped the tool call log at 5 MB with one rolled-over file, and recorded how long each call took.
- Added KillerBench to the installer's supported app list.

### Fixed

- Kept KillerMCP running when a tool fails unexpectedly or an unknown tool is called.
- Blocked scripts and remote content in agent-generated PDFs.
- Matched the setup dialog's title icon placement, caption height and padding to the current family dialogs.
- Hovered action buttons in the installer and the update and repair prompts fill with the accent gradient, and the Later and Deny buttons stay neutral on hover.

## [0.4.0] - 2026-10-11

### Added

- Added KillerBench tools for offline code lookups, read-only checks and full diagnostic scans, with long scans returned as jobs to poll and save as notes.
- Added guarded KillerBench repairs that stay off until the user turns them on in the KillerMCP section of the KillerBench About window.
- Required a fresh preview token, Windows administrator approval and, for disruptive repairs, a desktop approval window before any repair runs.
- Ran one repair at a time, recorded each request in a local action log, and locked further repairs after an uncertain result until the user clears the lock.

### Changed

- Set the wordmark in Punktype and Lacquer and the window headings, footer and setup text in Killer Sans, with the family title bar wordmark, grain and close button in setup.

### Fixed

- Made the release script publish by default, with `-DryRun` for local verification.
- Kept app icons sharp at fractional display DPI and app zoom.
- Restored the textured finish on the setup sidebar, setup frame and uninstall and error dialogs.
- Drew rounded button outlines without nicks at the corners.
- Smoothed the wordmark text in the update window, colored its MCP fuchsia, titled it "Update KillerMCP" when no update is selected and showed the new version in its title bar.
- Gave the installer and uninstaller their own taskbar buttons, labeled "KillerMCP Installer" and "Uninstall KillerMCP", with the app icon.

## [0.3.5] - 2026-09-30

### Added

- Labeled tool effects for MCP clients and recorded local tool call history without arguments or results.

### Fixed

- Allowed upgrades when a trusted prior setup file is newer than its stale uninstall entry.

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
