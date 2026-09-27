<p align="center">
  <a href="https://killertools.net/mcp"><img src="installer/Assets/mcp.png" width="150" alt="KillerMCP logo"></a><br>
  <a href="https://killertools.net/mcp"><img src="docs/wordmark.png" width="520" alt="KillerMCP"></a>
</p>

One local MCP connection for KillerTools and the Killer app family: KillerPDF, KillerNotes, KillerScan, KillerShell, Killendar, and KillerBench.

KillerMCP is a native .NET 10 host. It detects supported Killer apps, exposes their available commands, and registers one MCP server with compatible agent clients. The separate [KillerTools repository](https://github.com/SteveTheKiller/KillerTools) provides the shared engine for the website utilities.

## Install

[Download the signed KillerMCP installer](https://github.com/SteveTheKiller/KillerMCP/releases/latest/download/KillerMCP-Setup.exe), run setup, then open a new agent chat. Setup checks for the .NET 10 runtime and registers KillerMCP with Codex, Claude Code, Claude Desktop, Cursor, GitHub Copilot, Gemini CLI, and Windsurf when those clients are found.

Once connected, ask naturally:

- `killer domain search example.com`
- `killer merge these PDFs`
- `killerscan my network`
- `killer update status`

The server supplies tool descriptions and instructions that help the client select the appropriate tool. Client behavior varies, so these phrases are guidance rather than guaranteed commands.

KillerMCP checks GitHub Releases at most once a day when it starts. If a newer version is available, the agent receives an update notice and can provide the signed installer link. KillerMCP never downloads or installs an update without the user's approval.

## What it includes

- All 81 KillerTools website utilities from the native KillerTools engine.
- Automatic tool discovery for supported versions of KillerPDF, KillerScan, KillerNotes, KillerShell, Killendar, and KillerBench.
- Safe migration from the earlier Node based KillerMCP installation and client registrations.
- Install, reinstall, repair, and uninstall behavior through the KillerUI styled Windows setup.
- A framework dependent native package for each supported operating system and processor architecture.

KillerPDF provides PDF editing, conversion, inspection, OCR, printing, and accessibility tools when the installed version advertises them. KillerScan provides local network details, bounded scans, host probes, offline MAC vendor lookup, ping, route tracing, diagnostics, availability watching, and KillerSpeed tests. KillerShell can search files, list directories, inspect file details, and read bounded text. KillerNotes search, Killendar agenda lookup, and KillerBench reference tools appear when compatible app versions are installed.

See [What KillerMCP can do](docs/CAPABILITIES.md) for the complete tool guide, examples, limits, and data handling details.

## Build from source

Clone this repository with its pinned KillerTools engine source:

```powershell
git clone --recurse-submodules https://github.com/SteveTheKiller/KillerMCP.git
cd KillerMCP
```

Build and test the native host:

```powershell
dotnet build KillerMCP.slnx -c Release
powershell -NoProfile -File scripts\Test-All.ps1
```

Build a framework dependent package for the current platform:

```powershell
powershell -NoProfile -File scripts\Test-NativePackage.ps1
```

Build and test the Windows installer:

```powershell
powershell -NoProfile -File scripts\Test-WindowsInstaller.ps1
```

The native host requires the .NET 10 runtime. The Windows installer checks for it before installation and links to the official Microsoft download when it is missing.

For development builds or custom app locations, set `KILLERPDF_CLI`, `KILLERSCAN_CLI`, `KILLERNOTES_CLI`, `KILLENDAR_CLI`, `KILLERSHELL_CLI`, or `KILLERBENCH_CLI` to an absolute executable path. An invalid override leaves that app unavailable instead of silently selecting another copy.

Run `powershell -NoProfile -File release.ps1` to build, sign, verify, and test the exact release installer without publishing it. Add `-Publish` only after the reviewed source is on `origin/main` and the changelog entry is dated for release.

Public installers are published through [KillerMCP releases](https://github.com/SteveTheKiller/KillerMCP/releases).

## License

KillerMCP is licensed under the [GNU General Public License Version 3](LICENSE).
