# Changelog

All notable changes to KillerMCP are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.8.0] - Unreleased

KillerMCP 0.8.0 makes repairs, scans, passwords, recordings and updates safer, checks every tool call before it runs, and finds KillerScan's tools again.

### Fixed

- A KillerBench repair is reported as completed only when its full result shows KillerBench verified the change. An empty, malformed, cut-off or failed result is uncertain and locks further repairs, a lock file that cannot be read keeps repairs locked, and turning repairs off while a desktop approval is open stops the repair.
- Every tool call is checked against the tool's published inputs before anything runs. Unknown, missing, mistyped, too long and out of range values are refused, and arguments that are not an object are rejected.
- A scan with no target is held to the same 1,024 addresses as one with a target. KillerMCP checks the detected local network first and refuses a larger one instead of letting KillerScan scan all of it. Network scans, scan notes and scan PDFs report the range and the number of addresses probed, and a target must be four dotted numbers, so shorthand such as 192.168.1 is refused.
- PDF passwords and the signing certificate password never go on KillerPDF's command line. A KillerPDF too old to take them on standard input is not started for a call with a password and asks to be updated; calls without a password still work.
- KillerShot recordings have no sound unless the agent asks for it, instead of following KillerShot's saved setting, which can include the microphone. Screenshots are saved only where asked, and a copy goes into KillerShot's capture library only on request.
- Note text reaches KillerNotes in a file instead of on its command line whenever KillerNotes supports it, including short notes.
- Setup refuses an installation manifest that names a file outside the KillerMCP folder, through a parent path, drive, stream or link, before it reads or deletes anything.
- The update window downloads the installer and its checksum from the same release, refuses a download far larger than an installer, and gives up on one that stops arriving after three minutes. Right before it starts the installer, it checks the file again while holding it open so it cannot be replaced, and starts it only if it is setup for the offered version.
- A bcrypt compare is refused when the hash asks for a cost above 14, the most hashing allows, instead of running for seconds or longer.
- KillerMCP refuses a message larger than 16 MiB without reading it whole, runs at most 32 tool calls at once, and returns an error instead of a result too large to send.
- Requests that arrive together can no longer start more than four KillerBench jobs, and old jobs and unused repair previews are kept to a fixed number.
- KillerScan tools are found again with KillerScan 1.8.2 and later, and scan reports are offered only when KillerScan's scan tools are.
- KillerPDF tools return long document information and search results whole as data instead of cutting them into invalid JSON, and warnings KillerPDF prints while creating a file, such as bookmarks or form fields that could not be kept, reach the agent.
- KillerPDF 1.8 is no longer offered the split rules and flatten color options it does not support.
- Cross-app tools that read a PDF take files up to the same 64 MiB as the KillerPDF tools.
- The printer repair describes what it does: it clears offline mode on the listed queues.
- KillerMCP no longer offers KillerBench recipe repairs, which KillerBench refuses to run from a full scan, so the action could never succeed.
- The note export tool lists RTF, which KillerNotes already exports.
- The privacy policy explains that tool results go to the connected agent client, which may send them to its AI provider.

### Changed

- Reorganized the app into Features, Services and Models, with a root project and solution and fewer supporting libraries.

### Added

- Added Slovenian reference lookup results.

## [0.7.1] - 2026-10-20

### Fixed

- Setup releases its inherited installation-folder lock before replacing files during an update.
- WinGet publishing registers PSGallery when the build runner is missing it.
- Releases temporarily skip Microsoft Store checks and submission.
- Dry runs reject an undated changelog or a mismatched release date before building.

## [0.7.0] - 2026-10-20

### Fixed

- Updates install only when they are signed with the KillerMCP publisher's certificate from its certificate authority, with every certificate in the chain checked for revocation. A file trusted by Windows but signed by someone else, or by another authority's certificate in the same name, is refused, and an update whose check cannot finish waits for the next one.
- Setup registers agent clients all or nothing. A conflict in one client changes none, and a failure partway puts back the clients already changed. Uninstall removes KillerMCP from every client it can and lists the ones it could not change instead of stopping.
- Client settings that are empty or have trailing commas are read normally. A settings file with comments, such as Gemini CLI's, is left untouched and setup shows the exact entry to add.
- PDFs made from notes, reports and agent content render fully offline. A note with a refresh, base, frame, script or remote image can no longer redirect the page, reach a server or file share, or stop the PDF from being created.
- PDF passwords and the signing certificate password reach KillerPDF on standard input instead of its command line, where other programs and process logs could read them, when the installed KillerPDF supports it. Older KillerPDF versions still get them as before.
- File tools open network paths, mapped drives and links to them only on servers in the local network or listed in `%LOCALAPPDATA%\KillerMCP\network-servers.json`, so an agent cannot send your Windows sign-in to an outside server. Device paths are refused.
- KillerMCP answers right away when it starts, even when a Killer app is slow, hung or broken. Apps are asked for their commands in parallel, and one that does not answer is left out instead of delaying or closing the server. An app KillerMCP is still asking for its commands when the agent client closes it is stopped instead of left behind.
- A long tool call no longer holds up other requests. Calls run side by side, a client can cancel one and its app process stops, and a malformed line no longer closes KillerMCP.
- A tool call returns as soon as its app finishes, even when the app leaves a helper process running, instead of waiting for the timeout.
- The browser companion starts only when a browser tool is first used and keeps serving pages when other connections stall. Its page reports errors instead of failing silently, says when editor content is too large to send, describes the signature area to screen readers, and accepts a typed name as a signature.
- Agents can no longer turn on KillerBench repairs themselves. No tool may write into the KillerMCP or KillerBench settings folders, by any path, and the first repair after the KillerBench repair setting appears or changes asks for approval on the desktop.
- Only one KillerMCP window runs a KillerBench repair at a time. A repair lock that cannot be saved still blocks repairs in that window, and the result says the lock was not saved. A repair hold that cannot be opened is reported and no longer blocks later repairs. A repair that has started finishes even if the agent client closes or cancels the request, and KillerMCP stays open until it ends, so the PC is not left half repaired. A repair cancelled while it waits for approval on the desktop is recorded as not approved.
- Tools check page ranges, DPI, note IDs and MAC addresses before running an app, and a number sent as text gets a clear error. Saving PDF pages as notes reports the notes already made when a later page fails, and a failed MAC vendor lookup is reported as an error.
- Uninstall closes a running KillerMCP before removing it instead of failing partway and leaving a broken folder, and setup can now uninstall or reinstall over a folder an earlier uninstall left half removed.
- Setup closes running hosts right before it replaces the folder, so a host an agent client restarts during an upgrade no longer stops it. A finished install is no longer reported as failed when the old copy cannot be removed yet; the next install clears it.
- Setup reinstalls when its copy beside the install folder was deleted or quarantined, ends a client configuration that stops responding instead of waiting forever, and treats an unreadable update preference as the default instead of failing at every sign-in.
- Downloaded update installers no longer pile up in the temporary folder.
- Client settings keep their other entries as written instead of escaping their characters, a settings file that is a link stays a link, and a change a client saves while setup is updating its file is kept. Codex installed with npm is found and registered with the program built for the PC's processor, and a Codex that cannot be started or restored gets a clear message with the command to run.
- KillerMCP no longer closes when a client asks for something it does not offer, such as resource templates. It answers with a standard error, replies to ping, and treats tools/call without arguments as an empty set.
- The update window keeps other settings in the preferences file and shows a message when your choice cannot be saved instead of closing. After a failed automatic download its Open release button opens the release page, and Update now is unavailable while a download is already running, even when a newer update notice arrives during it, so a second installer no longer starts.
- Screen readers name the close buttons in setup and the Future updates choice in the update window, the .NET runtime download link in setup works from the keyboard, and the uninstall confirmation cancels on Enter or Escape instead of uninstalling.
- Uninstalling the Chocolatey package fails with instructions when KillerMCP's setup copy is missing, instead of reporting success and leaving KillerMCP and its agent connections in place.
- KillerMCP still reports a new release when it cannot save its update status, and the status file the update window reads is replaced whole instead of rewritten in place.
- A silent install or update that finds KillerMCP running exits with 1618, which Intune, Configuration Manager and RMM tools retry, instead of a general failure.

## [0.6.1] - 2026-10-19

### Fixed

- Updates started from an agent client such as Codex finish installing. Setup no longer stops partway when the client closes the KillerMCP hosts it replaces.
- The update window stays open when the agent client that started KillerMCP closes it.
- An update notice that closes before it opens is offered again at the next check instead of a day later.
- An update that cannot install shows why instead of closing silently.

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
