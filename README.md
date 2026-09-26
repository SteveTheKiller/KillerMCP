# KillerMCP

One local MCP connection for KillerTools and the Killer app family: KillerPDF, KillerNotes, KillerScan, KillerShell, and Killendar.

The goal is one Windows installer. It will detect installed apps, register one MCP server with supported agent clients, and expose useful tools through each app's own interface. The app repositories remain responsible for their CLI and service behavior. This repository owns the shared runtime, app adapters, installer, and integration checks.

## Current status

Development is in progress. This repository has a shared stdio host. It runs the bundled server from the [KillerTools site repository](https://github.com/SteveTheKiller/killer-tools-site) and adds app tools to the same connection. Current app adapters offer KillerPDF merging on the released 1.8 line, with preflight and accessibility reports when a build advertises those commands. KillerScan offers local network information and bounded scans. KillerShell file search, KillerNotes search, and Killendar agenda lookup appear when their CLI executables are available. Notes and calendar queries read the active database without changing it and currently cannot unlock encrypted files. A development Windows installer now packages the runtime, registers one MCP connection in Codex and Claude Code when their CLIs are available, and adds a Windows Installed Apps uninstall entry. Reinstall and uninstall preserve unexpected or modified runtime files. Other client registration, installed app CLI packaging, and final installer validation remain.

The public app websites will each have an `/mcp` page describing their tools and linking to the one KillerMCP setup. The single local connection does not require a separate MCP subdomain for each app.

## Development build

Build the KillerTools local MCP bundle first. Then run `node scripts/build.mjs <absolute path to killermcp.mjs>` in this repository. This stages the shared host, the KillerTools bundle, and app adapter files in `dist/`. The runtime looks for the app executables in their standard per-user and machine-wide install folders. It checks the KillerPDF, KillerScan, KillerNotes, and Killendar help output before listing their supported tools. For development builds or custom locations, set `KILLERPDF_CLI`, `KILLERSCAN_CLI`, `KILLERNOTES_CLI`, `KILLENDAR_CLI`, and `KILLERSHELL_CLI` to absolute executable paths. An invalid override leaves that app's tools unavailable instead of falling back to another copy.

On Windows x64 with Node 24.14.1, run `node scripts/build-portable.mjs` to add a Node executable and its license to `dist/portable/`. This development package runs without a Node installation on the target machine. Run `powershell -NoProfile -File scripts/build-installer.ps1` to embed it in the development installer. Run `powershell -NoProfile -File scripts/test-installer.ps1` to verify isolated installation, reinstall, Codex and Claude Code registration, MCP discovery, a tool call, and uninstall without changing either current client profile. The installer is not a release package yet.

The integration smoke script checks discovery, representative calls, released-version compatibility, and app detection. The scan check targets only loopback; the Notes and Killendar integration checks do not read personal databases. Set `KILLERMCP_SERVER` and `KILLERMCP_NODE` to test a copied portable package.

Once connected, people can ask the agent in ordinary language, such as `killer domain search example.com`, `killer merge these PDFs`, or `killerscan 192.168.8.0/24`. The server supplies tool descriptions and instructions to help the client select a tool. Client behavior varies, so these phrases are guidance rather than guaranteed commands.

Run `node scripts/discovery-test.mjs` to check standard install-folder detection and development overrides. KillerNotes 1.3.1, Killendar 1.1.4, and KillerShell 1.2.6 serve their commands from their installed executables with `--cli` after those app versions are installed.

No release or installation instructions are available until the packaged runtime and installer are verified.
