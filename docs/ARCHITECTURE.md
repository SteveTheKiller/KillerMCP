# KillerMCP architecture

`KillerMCP.csproj` and `KillerMCP.slnx` live at the repository root. The main project compiles the host, its tools and app integrations. Its Solution Explorer view also exposes the helper programs, installer, tests and documentation.

## Source map

```text
KillerMCP.csproj
KillerMCP.slnx
Program.cs
Features/
  Browser/       Local browser companion and its embedded page
  Tools/         Built-in tool definitions, dispatch and handlers
  Killendar/     Calendar integration
  KillerBench/   Diagnostics and guarded repair integration
  KillerNotes/   Notes integration
  KillerPDF/     PDF integration
  KillerScan/    Network scanning integration
  KillerShell/   File and system integration
  KillerShot/    Screenshot and recording integration
  Workflows/     Work across multiple installed apps
Services/
  Mcp/           Request loop, app catalog and tool registry
  ...            Discovery, process lifetime, logging, safety and updates
Models/          Tool contracts and request errors
Configuration/   Client settings and the configuration helper
Shell/           Windows tray, updates and repair approval
installer/       Windows setup and uninstall
dependencies/    Pinned shared KillerTools engine source
tests/           Client and app integration checks
scripts/         Build, package and verification commands
```

## Request flow

`Program.cs` starts `McpServer`. The server initializes update monitoring and loads installed app tools through `AppCatalog`, then reads MCP requests from standard input and writes complete JSON-RPC responses to standard output.

`ToolRegistry` combines built-in tools with available app tools. It checks file access, records call outcomes and formats results. `BuiltInTools` owns the native tool definitions and handlers; app adapters under `Features` call the installed apps through the shared process runner.

Tool calls retain cancellation by request ID. App discovery runs in parallel with a timeout. Closing the host stops its ordinary child processes; already approved KillerBench repairs retain their existing lifetime and recovery rules.

## Executables

| Project | Output | Responsibility |
| --- | --- | --- |
| `KillerMCP.csproj` | `KillerMCP` | Cross-platform MCP host |
| `Configuration/KillerMCP.Configure.csproj` | `KillerMCP.Configure` | Register or unregister the host with agent clients |
| `Shell/KillerMCP.Notify.csproj` | `KillerMCP.Notify.exe` | Independent Windows tray, update and repair approval process |
| `installer/KillerMCP.Setup.csproj` | `KillerMCP-Setup.exe` | Install, repair, update and remove the Windows package |

The configuration helper keeps its `register|unregister <absolute host path>` command and exit codes. The notifier keeps its existing launch arguments and can outlive an agent-host process. Packaging preserves these executable names.

The former Runtime library is compiled into the main host. Client registration is compiled into the configuration helper. Tests reference these actual executable assemblies. The separate KillerTools repository remains a pinned dependency and keeps its own engine project.

## Build and verification

Build `KillerMCP.slnx` from the root. This builds the host, configuration helper, notifier and checks. The installer is built through `scripts/Build-WindowsInstaller.ps1`, which first prepares its embedded native package.

Run the client and runtime check projects plus `scripts/Test-NativeHost.mjs` to verify registration, integrations and MCP behavior. `scripts/Test-NativePackage.ps1` verifies the published executables. Installer and update integration checks belong in the disposable Windows lab.
