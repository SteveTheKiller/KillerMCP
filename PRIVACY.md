# KillerMCP privacy policy

Last updated: October 21, 2026

This policy covers the installed Windows edition of KillerMCP and its Claude plugin. Steve the Killer operates KillerMCP. The hosted KillerTools server at mcp.killertools.net has its own [privacy policy](https://mcp.killertools.net/privacy).

## What KillerMCP collects

KillerMCP has no accounts, analytics or telemetry. It runs on your computer, and it sends your tool arguments, files, notes and results nowhere itself unless a tool you call needs a network service, as described below.

Tool results do go back to the agent client you connected KillerMCP to, such as Claude, Codex or Gemini CLI, because that is how the agent uses them. A file read, note, screenshot path or scan report returned by a tool is then part of your conversation, and the client may send it to its AI provider under that provider's policy. Choose what you let an agent read accordingly. The only requests that reach servers Steve the Killer runs are the CVE and GIF searches, which pass through his proxies, and the KillerScan speed test.

## Network services KillerMCP contacts

KillerMCP contacts a service only when it checks for updates or when a tool you call needs it.

- **GitHub:** KillerMCP checks the GitHub releases API for updates and downloads updates from GitHub releases. The KillerScripts and Killer modules tools read their lists from GitHub.
- **Domain lookups:** DNS lookups go to Cloudflare DNS over HTTPS. Registration lookups go to IANA's RDAP bootstrap file and the public RDAP server for the domain.
- **CVE lookups:** Your search text goes to the KillerTools NVD proxy, which queries the National Vulnerability Database.
- **GIF search:** Your search text goes to the KillerTools GIF proxy, which queries a GIF search provider.
- **KillerScan:** KillerScan contacts the hosts and networks you ask it to probe. Its speed test exchanges test data with Cloudflare's speed test and the KillerScan speed test server. It can refresh its MAC vendor list from the public Nmap and Wireshark data files.

Each of these services receives your IP address with the request, as any web request does, and handles it under its own policy.

## Data stored on your computer

KillerMCP writes a tool call log to `%LOCALAPPDATA%\KillerMCP\tool-calls.jsonl`. Each entry records the time, the tool name, whether the call finished or failed, and how long it took. Arguments and results are never logged, and the log is never uploaded. The installed Killer apps keep their own data, such as notes and calendar entries, on your computer.

## Contact

Send privacy questions to [steve@thekiller.net](mailto:steve@thekiller.net) or open an issue on [GitHub](https://github.com/SteveTheKiller/KillerMCP/issues).
