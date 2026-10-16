# KillerMCP for Claude

KillerMCP gives Claude one local connection to KillerTools and the Killer apps installed on your Windows computer. It includes all 81 KillerTools utilities, such as subnet math, DNS and RDAP lookups, Windows error and event codes, PowerShell building and data formatting. It also adds tools from each supported app it finds: KillerPDF, KillerNotes, KillerScan, KillerShell, Killendar and KillerBench.

## Requirements

This plugin needs Windows and the KillerMCP app. Install the app first with the [signed installer](https://github.com/SteveTheKiller/KillerMCP/releases/latest/download/KillerMCP-Setup.exe), WinGet or Chocolatey:

```powershell
winget install SteveTheKiller.KillerMCP
choco install killermcp
```

App tools appear only for the Killer apps that are installed. Your organization may block local MCP servers on managed computers.

The plugin version matches the KillerMCP app release version. Native packaging and release checks stop if the plugin version differs from `src/KillerMCP/KillerMCP.csproj` or its public author is not Steve the Killer. The hosted Killer Tools plugin is versioned separately.

## What the plugin runs

The plugin starts one local MCP server. It runs `start-killermcp.ps1` with Windows PowerShell, and that script starts the installed `KillerMCP.exe` from `%LOCALAPPDATA%\Programs\KillerMCP`. If the app is missing, the script stops and says so. The plugin contains no other code and installs nothing.

## What the tools can change

Each tool carries read-only, destructive and open-world annotations, so review a tool call before you approve it.

- KillerShell tools read local files and Windows information. They cannot run commands or change the system.
- KillerPDF tools can create new PDF files and print. They refuse to overwrite an existing file.
- KillerNotes tools can create and change notes and export files.
- Killendar can create an appointment in the open app.
- KillerScan probes your local network, and its speed test can transfer a large amount of data.
- KillerBench checks only read Windows state. KillerBench repairs change Windows and stay off until you turn them on. Most of them ask for administrator approval.

## What it sends over the network

KillerMCP has no account and no analytics. It contacts these services only when a tool needs them:

- GitHub, to check for KillerMCP updates and to list KillerScripts and Killer modules.
- Cloudflare DNS over HTTPS and public RDAP registries, for domain lookups.
- The KillerTools CVE and GIF proxies, which receive only your search text.
- The hosts and networks you ask KillerScan to probe, Cloudflare's speed test and the KillerScan speed test server, and the public Nmap and Wireshark MAC vendor lists.

KillerMCP keeps a local log of tool names and timings in `%LOCALAPPDATA%\KillerMCP\tool-calls.jsonl`. The log never includes arguments or results, and it is never uploaded. See the [privacy policy](https://github.com/SteveTheKiller/KillerMCP/blob/main/PRIVACY.md) for details.

## Links

- [Everything KillerMCP can do](https://github.com/SteveTheKiller/KillerMCP/blob/main/docs/CAPABILITIES.md)
- [Report a problem](https://github.com/SteveTheKiller/KillerMCP/issues)
