# KillerMCP

One local MCP connection for KillerTools and the Killer app family: KillerPDF, KillerNotes, KillerScan, KillerShell, Killendar, and KillerBench.

The goal is one Windows installer. It will detect installed apps, register one MCP server with supported agent clients, and expose useful tools through each app's own interface. The app repositories remain responsible for their CLI and service behavior. This repository owns the shared runtime, app adapters, installer, and integration checks.

## Current status

Development is in progress. This repository has a shared stdio host. It runs the bundled server from the [KillerTools site repository](https://github.com/SteveTheKiller/killer-tools-site) and adds app tools to the same connection. Current app adapters offer KillerShell file search, KillerBench code lookups, and KillerScan local network information when those apps' CLI executables are available. KillerPDF, KillerNotes, and Killendar adapters and the shared installer are not ready yet.

The six app websites will each have an `/mcp` page describing their tools and linking to the one KillerMCP setup. The single local connection does not require a separate MCP subdomain for each app.

## Development build

Build the KillerTools local MCP bundle first. Then run `node scripts/build.mjs <absolute path to killermcp.mjs>` in this repository. This stages the shared host, the KillerTools bundle, and app adapter files in `dist/`. Set `KILLERSHELL_CLI`, `KILLERBENCH_CLI`, and `KILLERSCAN_CLI` to absolute paths of the apps' CLI executables to make their tools available.

On Windows x64 with Node 24.14.1, run `node scripts/build-portable.mjs` to add a Node executable and its license to `dist/portable/`. This development package runs without a Node installation on the target machine. It is not an installer or release package yet.

Run `node scripts/smoke.mjs <absolute KillerShell CLI path> <absolute search test directory> <absolute KillerBench CLI path> <absolute KillerScan CLI path>` to check tool discovery, representative calls, and app detection. Set `KILLERMCP_SERVER` and `KILLERMCP_NODE` to test a copied portable package. The portable runtime passed this check from a folder outside the repositories.

No release or installation instructions are available until the packaged runtime and installer are verified.
