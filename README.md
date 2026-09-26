# KillerMCP

One local MCP connection for KillerTools and the Killer app family: KillerPDF, KillerNotes, KillerScan, KillerShell, Killendar, and KillerBench.

The goal is one Windows installer. It will detect installed apps, register one MCP server with supported agent clients, and expose useful tools through each app's own interface. The app repositories remain responsible for their CLI and service behavior. This repository owns the shared runtime, app adapters, installer, and integration checks.

## Current status

Development is in progress. This repository has a shared stdio host. It runs the bundled server from the [KillerTools site repository](https://github.com/SteveTheKiller/killer-tools-site) and adds app tools to the same connection. Current app adapters offer KillerPDF merging on the released 1.8 line, with preflight and accessibility reports when a build advertises those commands. KillerScan offers local network information and bounded scans. KillerShell file search, KillerBench code lookups, KillerNotes search, and Killendar agenda lookup appear when their CLI executables are available. Notes and calendar queries read the active database without changing it and currently cannot unlock encrypted files. A development Windows installer now packages the runtime and registers one Codex connection when the Codex CLI is available. Uninstall, other client registration, installed app CLI packaging, and final installer validation remain.

The six app websites will each have an `/mcp` page describing their tools and linking to the one KillerMCP setup. The single local connection does not require a separate MCP subdomain for each app.

## Development build

Build the KillerTools local MCP bundle first. Then run `node scripts/build.mjs <absolute path to killermcp.mjs>` in this repository. This stages the shared host, the KillerTools bundle, and app adapter files in `dist/`. The runtime looks for the app executables in their standard per-user and machine-wide install folders. It checks the KillerPDF, KillerScan, KillerNotes, and Killendar help output before listing their supported tools. For development builds or custom locations, set `KILLERPDF_CLI`, `KILLERSCAN_CLI`, `KILLERNOTES_CLI`, `KILLENDAR_CLI`, `KILLERSHELL_CLI`, and `KILLERBENCH_CLI` to absolute executable paths. An invalid override leaves that app's tools unavailable instead of falling back to another copy.

On Windows x64 with Node 24.14.1, run `node scripts/build-portable.mjs` to add a Node executable and its license to `dist/portable/`. This development package runs without a Node installation on the target machine. Run `powershell -NoProfile -File scripts/build-installer.ps1` to embed it in the development installer. Run `powershell -NoProfile -File scripts/test-installer.ps1` to verify isolated installation, reinstall, Codex registration, MCP discovery, and a tool call without changing the current Codex profile. The installer is not a release package yet.

Run `node scripts/smoke.mjs <KillerShell CLI> <search directory> <KillerBench CLI> <KillerScan executable> <KillerPDF development executable> <test PDF> <KillerPDF 1.8 executable> <second test PDF> <KillerNotes CLI> <Killendar CLI>` with absolute paths to check discovery, representative calls, released-version compatibility, and app detection. The scan check targets only loopback; the Notes and Killendar integration checks do not read personal databases. Set `KILLERMCP_SERVER` and `KILLERMCP_NODE` to test a copied portable package.

Once connected, people can ask the agent in ordinary language, such as `killer domain search example.com`, `killer merge these PDFs`, or `killerscan 192.168.8.0/24`. The server supplies tool descriptions and instructions to help the client select a tool. Client behavior varies, so these phrases are guidance rather than guaranteed commands.

Run `node scripts/discovery-test.mjs` to check standard install-folder detection and development overrides. KillerShell and KillerBench still need to include their CLI executables in installed app packages before automatic detection can offer their tools to end users.

No release or installation instructions are available until the packaged runtime and installer are verified.
