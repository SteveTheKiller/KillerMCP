# KillerMCP

One local MCP connection for KillerTools and the Killer app family: KillerPDF, KillerNotes, KillerScan, KillerShell, Killendar, and KillerBench.

The goal is one Windows installer. It will detect installed apps, register one MCP server with supported agent clients, and expose useful tools through each app's own interface. The app repositories remain responsible for their CLI and service behavior. This repository owns the shared runtime, app adapters, installer, and integration checks.

## Current status

Development is in progress. This repository has a shared stdio host. It runs the bundled server from the [KillerTools site repository](https://github.com/SteveTheKiller/killer-tools-site) and adds app tools to the same connection. The first app adapter offers read-only KillerShell file search when its CLI is available. The other five app adapters and the shared installer are not ready yet.

The six app websites will each have an `/mcp` page describing their tools and linking to the one KillerMCP setup. The single local connection does not require a separate MCP subdomain for each app.

## Development build

Build the KillerTools local MCP bundle first. Then run `node scripts/build.mjs <absolute path to killermcp.mjs>` in this repository. This stages the shared host, the KillerTools bundle, and app adapter files in `dist/`. Set `KILLERSHELL_CLI` to the absolute path of the built KillerShell CLI executable to make its search tool available. The staged host currently requires Node 24.

Run `node scripts/smoke.mjs <absolute KillerShell CLI path> <absolute search test directory>` to check tool discovery, a KillerTools call, a KillerShell search, and app detection. The packaged runtime passed this check from a folder outside the repositories.

No release or installation instructions are available until the packaged runtime and installer are verified.
