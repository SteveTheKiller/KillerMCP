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

Tool calls retain cancellation by request ID. App and network calls run asynchronously, while eight dedicated slots keep bounded local tools responsive beside up to 32 other calls. Update checks run in the background and failed checks are cached for 15 minutes. App discovery runs in parallel with a timeout. Closing the host stops its ordinary child processes; already approved KillerBench repairs retain their existing lifetime and recovery rules. Child input is always redirected and closed when no input is supplied.

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

Packaging, installer checks and `release.ps1` share `-ArtifactRoot`, defaulting to `scratch/KillerMCP-packaging` under the current user's home directory. Packages, staging, installers, checksums and release notes stay there. Output inside a Git repository is rejected. Existing `-Installer` inputs remain supported for testing an exact package.

Run the client and runtime check projects plus `scripts/Test-NativeHost.mjs` to verify registration, integrations and MCP behavior. `scripts/Test-NativePackage.ps1` verifies the published executables. Installer and update integration checks belong in the disposable Windows lab.

The full suite runs in that lab with `powershell -NoProfile -File scripts\Test-All.ps1 -LabMachine <lab name>`. It first requires a passed GitHub Checks run for the exact commit, so push the reviewed commit and let Checks finish before starting it. The maintained [fuzz and data-move drivers](../scripts/review/README.md) take an explicit candidate host, scratch root and seed.

For development builds or custom app locations, set `KILLERPDF_CLI`, `KILLERSCAN_CLI`, `KILLERNOTES_CLI`, `KILLENDAR_CLI`, `KILLERSHELL_CLI`, `KILLERBENCH_CLI`, or `KILLERSHOT_CLI` to an absolute executable path. An invalid override leaves that app unavailable instead of silently selecting another copy.

`release.ps1` builds, signs, verifies, publishes to GitHub and submits to the Microsoft Store. `-DryRun` builds and signs without publishing, and `-SkipSign` produces an unsigned local build that never publishes.
