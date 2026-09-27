<p align="center">
  <a href="https://killertools.net/mcp"><img src="installer/Assets/mcp.png" width="150" alt="KillerMCP logo"></a><br>
  <a href="https://killertools.net/mcp"><img src="docs/wordmark.svg" width="520" alt="KillerMCP"></a>
</p>

One local MCP connection for KillerTools and the Killer app family: KillerPDF, KillerNotes, KillerScan, KillerShell, and Killendar.

KillerMCP detects installed apps, registers one MCP server with supported agent clients, and exposes useful tools through each app's own interface. The app repositories remain responsible for their CLI and service behavior. This repository owns the shared runtime, app adapters, installer, and integration checks.

## Install

[Download the signed KillerMCP installer](https://github.com/SteveTheKiller/KillerMCP/releases/latest/download/KillerMCP-Setup.exe), run setup, then open a new agent chat. Setup registers KillerMCP with Codex, Claude Code, Claude Desktop, Cursor, GitHub Copilot, Gemini CLI, and Windsurf when those clients are found.

Once connected, ask naturally:

- `killer domain search example.com`
- `killer merge these PDFs`
- `killerscan 192.168.8.0/24`

The server supplies tool descriptions and instructions to help the client select the appropriate tool. Client behavior varies, so these phrases are guidance rather than guaranteed commands.

## Current status

Version 0.1.0 has a shared stdio host. It runs the bundled server from the [KillerTools site repository](https://github.com/SteveTheKiller/killer-tools-site) and adds app tools to the same connection. Current app adapters offer KillerPDF merging on the released 1.8 line, with preflight and accessibility reports when a build advertises those commands. KillerScan offers local network information and bounded scans. KillerShell file search, KillerNotes search, and Killendar agenda lookup appear when their CLI executables are available. Notes and calendar queries read the active database without changing it and currently cannot unlock encrypted files. The signed Windows installer packages the runtime, registers one MCP connection in Codex, Claude Code, Claude Desktop, Cursor, GitHub Copilot, Gemini CLI, and Windsurf when those clients are available, and adds a Windows Installed Apps uninstall entry. The exact signed installer passes isolated install, reinstall, seven client registrations, MCP discovery, representative calls, modified-file protection, uninstall, and clean Windows Sandbox testing.

The public app websites each have an `/mcp` page describing their tools and linking to the same KillerMCP setup. The single local connection does not require a separate MCP subdomain for each app.

See [What KillerMCP can do](docs/CAPABILITIES.md) for a human-readable guide to every KillerTools utility, the public app integrations, example requests, limits, and data handling.

## Build from source

Build the KillerTools local MCP bundle first. Then run `node scripts/build.mjs <absolute path to killermcp.mjs>` in this repository. This stages the shared host, the KillerTools bundle, and app adapter files in `dist/`. The runtime looks for the app executables in their standard per-user and machine-wide install folders. It checks the KillerPDF, KillerScan, KillerNotes, and Killendar help output before listing their supported tools. For development builds or custom locations, set `KILLERPDF_CLI`, `KILLERSCAN_CLI`, `KILLERNOTES_CLI`, `KILLENDAR_CLI`, and `KILLERSHELL_CLI` to absolute executable paths. An invalid override leaves that app's tools unavailable instead of falling back to another copy.

On Windows x64 with Node 24.14.1, run `node scripts/build-portable.mjs` to add a Node executable and its license to `dist/portable/`. This portable package runs without a Node installation on the target machine. Run `powershell -NoProfile -File scripts/build-installer.ps1` to embed it in an unsigned local test installer. Run `powershell -NoProfile -File scripts/test-installer.ps1` to verify isolated installation, reinstall, seven client registrations, preservation of unrelated client settings, MCP discovery, a tool call, and uninstall without changing current client profiles.

Run `powershell -NoProfile -File release.ps1` to rebuild the KillerTools bundle, build and sign KillerMCP Setup with SimplySign, verify its timestamp and trust chain, repeat the exact installer tests, and write `SHA256SUMS.txt`. This command does not publish. Add `-Publish` only after `main` has been reviewed and pushed to create the version tag and GitHub release.

The integration smoke script checks discovery, representative calls, released-version compatibility, and app detection. The scan check targets only loopback; the Notes and Killendar integration checks do not read personal databases. Set `KILLERMCP_SERVER` and `KILLERMCP_NODE` to test a copied portable package.

Run `node scripts/discovery-test.mjs` to check standard install-folder detection and development overrides. KillerNotes 1.3.1, Killendar 1.1.4, and KillerShell 1.2.6 serve their commands from their installed executables with `--cli` after those app versions are installed.

Public installers are published through [KillerMCP releases](https://github.com/SteveTheKiller/KillerMCP/releases).

## License

KillerMCP is licensed under the [GNU General Public License Version 3](LICENSE).
