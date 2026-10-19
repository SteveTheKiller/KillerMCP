# Changelog

All notable changes to KillerMCP are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.6.1] - Unreleased

### Fixed

- Updates started from an agent client such as Codex finish installing. Setup no longer stops partway when the client closes the KillerMCP hosts it replaces.

## [0.6.0] - 2026-10-19

### Added

- Setup lets you choose whether updates are checked, downloaded, or installed automatically, and keeps your existing choice during upgrades.
- KillerShot tools list monitors and windows, take screenshots of a monitor, every monitor, a window or a rectangle, record MP4 or GIF for a set time, list recent captures, open an image in the KillerShot editor, and read KillerShot's settings.
- KillerPDF tools cover its 1.9 commands: optimize, page numbers and Bates stamps, booklets and n-up sheets, bookmarks from headings, Convert Scan to Text, reverse, upright, straighten and divide pages, partial rasterizing, redaction, protection, signing with a trusted timestamp, export to Word, Excel, PowerPoint, HTML, Markdown, text and JSON, Office to PDF, data merge, text replacement, form data, field recognition, XFA conversion, form flattening, macros, scanning, layers, attachments, portfolios, links, contents pages, page labels and the initial view.
- KillerPDF reports cover print production, signatures, layers, portfolios, comments, navigation, measurements, separations, attachments, recognized fields and structural comparison.
- killerpdf_split splits by page count, equal parts, bookmark level or file size, and killerpdf_flatten takes color mode, compression and a scanned look.
- killerpdf_scan_to_text can write each converted word as scanned beside it as text for review, and keep the words a review names as scanned.
- killerpdf_save_text_as_note exports a PDF's text as Markdown and saves it as one KillerNotes note.
- killerpdf_save_report_as_note saves a KillerPDF report, such as a PDF's signatures, comments, measurements or preflight findings, as a KillerNotes note.
- killernotes_export_notes_pdf exports several KillerNotes notes into one PDF, in the order given.
- killerpdf_images_to_pdf turns PNG, JPEG, TIFF, BMP and GIF images, such as KillerShot captures, into one PDF.

### Changed
- The keyboard focus outline is a thin solid ring in the theme accent instead of a thick dotted line, and uses the text color where the accent would be too faint to see.

### Fixed

- Silent installs refuse a setup without a valid signature and return 1625 instead of installing.
- Split and data merge reject file name templates that could write outside the output folder.
- Keyboard focus stays visible on setup, update, and repair buttons.
- The Claude plugin starts on Windows clients with the default PowerShell execution policy.
- KillerPDF tools recognize current command help, preserve renamed macro PDFs, and write folder outputs across drives. Bates stamping rejects duplicate file names.
- Reports and notes use a file when escaped text would exceed the command line, support up to 500,000 characters, and clean up failed handoffs.
- KillerShot captures always save a file, and monitor, image, PDF, macro, and signature inputs match their advertised validation.
- The startup update check honors the saved update behavior.
- Release dry runs stop before building when local main has not been pushed or differs from origin.

## [0.5.1] - 2026-10-14

### Added

- killerpdf_verify_install checks every installed KillerPDF program file against the SHA-256 hashes in its payload manifest and reports missing, changed, or extra files.
- Later and the close button keep an offered update in the notification area, where the KillerMCP icon reopens the update window, opens the release notes, or exits.
- The update window lists what is new in the offered version, and its version number and Full release notes link open that release on GitHub.

### Changed

- Setup and the update window show the KillerMCP version alone under the wordmark, centered and a size larger, matching the KillerPDF installer.
- The setup sidebar labels its app list "Works with" in one line instead of a three-line description, so seven apps fit above the footer, and lists KillerShot in teal.
- Release builds now publish and check the native package once instead of twice.

### Fixed

- Updated System.Security.Cryptography.Pkcs to 10.0.13 in the KillerTools engine for the GHSA-846f-f764-76mx and GHSA-2fmv-rv2c-59x7 advisories.
- Upgrades repair an uninstall entry that overlapping installs left at an older version, when the signed setup copy matches the installed files, instead of failing with a changed setup file error.
- Agent clients that start several KillerMCP hosts at once now get one update window instead of one per host, and a later notice brings the open window forward.
- The update and repair approval windows show the KillerMCP icon and their own name on the taskbar instead of a generic KillerMCP.Notify button.
- Installer and update-notifier icon buttons expose their tooltip names to screen readers and show keyboard focus.
- Silent installs return specific Windows error codes and reject a second concurrent install instead of reporting a general failure.

## [0.5.0] - 2026-10-12

0.5.0 brings KillerMCP to WinGet and Chocolatey and hardens the host against failed tools and untrusted PDF content.

### Added

- Added WinGet and Chocolatey packages, submitted automatically when a release is published.

### Changed

- Network report PDFs now return the scan target and device count, and network report notes use the same device table as the PDF.
- Capped the tool call log at 5 MB with one rolled-over file, and recorded how long each call took.
- Added KillerBench to the installer's supported app list.

### Fixed

- Blocked scripts and remote content in agent-generated PDFs.
- Kept KillerMCP running when a tool fails unexpectedly or an unknown tool is called.
- Limited KillerNotes content to 30,000 characters and returned a clear error for requests too large to pass to an app.
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
