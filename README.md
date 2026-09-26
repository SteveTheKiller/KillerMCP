# KillerMCP

One local MCP connection for KillerTools and the Killer app family: KillerPDF, KillerNotes, KillerScan, KillerShell, Killendar, and KillerBench.

The goal is one Windows installer. It will detect installed apps, register one MCP server with supported agent clients, and expose useful tools through each app's own interface. The app repositories remain responsible for their CLI and service behavior. This repository owns the shared runtime, app adapters, installer, and integration checks.

## Current status

Development is in progress. KillerTools has a bundled local MCP server in the [KillerTools site repository](https://github.com/SteveTheKiller/killer-tools-site). A read-only KillerShell search CLI is the first desktop app interface. The shared installer and six app adapters are not ready yet.

No release or installation instructions are available until the packaged runtime and installer are verified.
