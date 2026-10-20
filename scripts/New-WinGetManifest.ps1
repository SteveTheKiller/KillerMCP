param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v\d+\.\d+\.\d+$')]
    [string]$Tag,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$version = $Tag.Substring(1)
$headers = @{
    Accept = 'application/vnd.github+json'
    'User-Agent' = 'KillerMCP-WinGet-Release'
    'X-GitHub-Api-Version' = '2022-11-28'
}
if ($env:GITHUB_TOKEN) { $headers.Authorization = "Bearer $env:GITHUB_TOKEN" }
$release = Invoke-RestMethod "https://api.github.com/repos/SteveTheKiller/KillerMCP/releases/tags/$Tag" -Headers $headers
if ($release.draft -or $release.prerelease -or $release.tag_name -ne $Tag) {
    throw 'WinGet requires a published stable release matching the requested tag.'
}
$assets = @($release.assets | Where-Object { $_.name -eq 'KillerMCP-Setup.exe' })
if ($assets.Count -ne 1) { throw 'The release must contain exactly one KillerMCP-Setup.exe installer.' }
$url = "https://github.com/SteveTheKiller/KillerMCP/releases/download/$Tag/KillerMCP-Setup.exe"
if ($assets[0].browser_download_url -ne $url) { throw 'Unexpected installer download URL.' }

$directory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $directory) { throw 'Use a new output directory to avoid submitting stale manifests.' }
[IO.Directory]::CreateDirectory($directory) | Out-Null
$installerPath = "$directory.exe"
if (Test-Path -LiteralPath $installerPath) { throw 'The installer download path already exists.' }
Invoke-WebRequest -Uri $url -OutFile $installerPath -UseBasicParsing
# WinGet publishes the hash of this download, so it has to match the release digest and carry the
# KillerMCP signature.
& (Join-Path $PSScriptRoot 'Assert-ReleaseInstaller.ps1') -Path $installerPath
$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
if (-not $assets[0].digest) { throw 'The GitHub release does not list a digest for the installer.' }
if ($assets[0].digest -ne "sha256:$($hash.ToLowerInvariant())") {
    throw 'Downloaded installer does not match the GitHub release digest.'
}
$date = ([datetime]$release.published_at).ToUniversalTime().ToString('yyyy-MM-dd')
$utf8 = New-Object System.Text.UTF8Encoding($false)

# Setup installs for the current user without elevation. Exit code 10 means the .NET 10
# runtime is missing, which WinGet resolves through the declared dependency.
$installer = @"
# Created by the KillerMCP release workflow
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.12.0.schema.json

PackageIdentifier: SteveTheKiller.KillerMCP
PackageVersion: $version
InstallerType: exe
Scope: user
InstallModes:
- silent
- silentWithProgress
InstallerSwitches:
  Silent: /silent
  SilentWithProgress: /silent
ExpectedReturnCodes:
- InstallerReturnCode: 10
  ReturnResponse: missingDependency
UpgradeBehavior: install
Dependencies:
  PackageDependencies:
  - PackageIdentifier: Microsoft.DotNet.DesktopRuntime.10
    MinimumVersion: 10.0.0
AppsAndFeaturesEntries:
- DisplayName: KillerMCP
  Publisher: Steve the Killer
  DisplayVersion: $version
  ProductCode: KillerMCP
ReleaseDate: $date
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $hash
ManifestType: installer
ManifestVersion: 1.12.0
"@
$releaseNotes = ([string]$release.body).Trim()
if ([string]::IsNullOrWhiteSpace($releaseNotes) -or $releaseNotes.Length -gt 10000) {
    throw 'WinGet requires release notes between 1 and 10000 characters.'
}
$releaseNotes = (($releaseNotes -split '\r?\n') | ForEach-Object { "  $_" }) -join "`r`n"
$locale = @"
# Created by the KillerMCP release workflow
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.12.0.schema.json

PackageIdentifier: SteveTheKiller.KillerMCP
PackageVersion: $version
PackageLocale: en-US
Publisher: Steve the Killer
PublisherUrl: https://github.com/SteveTheKiller
PublisherSupportUrl: https://github.com/SteveTheKiller/KillerMCP/issues
Author: Steve the Killer
PackageName: KillerMCP
PackageUrl: https://killertools.net/mcp
License: GPL-3.0
LicenseUrl: https://github.com/SteveTheKiller/KillerMCP/blob/HEAD/LICENSE
Copyright: Copyright (c) 2026 Steve the Killer
ShortDescription: One local MCP connection for KillerTools and the Killer app family.
Description: KillerMCP gives MCP compatible agents one local connection to the 81 KillerTools utilities and to KillerPDF, KillerScan, KillerNotes, KillerShell, Killendar, KillerBench, and KillerShot when they are installed. Setup registers it with supported agent clients such as Codex, Claude Code, Claude Desktop, Cursor, GitHub Copilot, Gemini CLI, and Windsurf. It installs for the current user. No account, no telemetry. Open source under GPLv3.
Moniker: killermcp
Tags:
- ai
- claude
- codex
- mcp
- mcp-server
- opensource
- windows
ReleaseNotes: |-
$releaseNotes
ReleaseNotesUrl: https://github.com/SteveTheKiller/KillerMCP/releases/tag/$Tag
ManifestType: defaultLocale
ManifestVersion: 1.12.0
"@
$manifest = @"
# Created by the KillerMCP release workflow
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.12.0.schema.json

PackageIdentifier: SteveTheKiller.KillerMCP
PackageVersion: $version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.12.0
"@
[IO.File]::WriteAllText((Join-Path $directory 'SteveTheKiller.KillerMCP.installer.yaml'), ($installer -replace '\r?\n', "`r`n"), $utf8)
[IO.File]::WriteAllText((Join-Path $directory 'SteveTheKiller.KillerMCP.locale.en-US.yaml'), ($locale -replace '\r?\n', "`r`n"), $utf8)
[IO.File]::WriteAllText((Join-Path $directory 'SteveTheKiller.KillerMCP.yaml'), ($manifest -replace '\r?\n', "`r`n"), $utf8)
Write-Host "Generated WinGet manifests for $Tag with SHA256 $hash in $directory"
