# Changelog

All notable changes to KillerMCP are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.1] - Unreleased

0.2.1 expands KillerShell file access through the shared MCP connection.

### Added

- Added KillerShell tools for directory listings, file details, and bounded text reading.

## [0.2.0] - 2026-09-27

0.2.0 replaces the bundled Node server with a smaller native .NET 10 host.

### Added

- Added the native KillerTools engine, daily update checks, KillerBench tools, and complete KillerScan command coverage.

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
