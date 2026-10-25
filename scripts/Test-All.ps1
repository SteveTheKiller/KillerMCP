param([string]$ArtifactRoot = '')

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ArtifactRoot = & (Join-Path $PSScriptRoot 'Get-ArtifactRoot.ps1') -ArtifactRoot $ArtifactRoot
& (Join-Path $PSScriptRoot 'Test-PackagingPaths.ps1') -ScratchRoot $ArtifactRoot

dotnet build (Join-Path $root 'KillerMCP.slnx') -c Release
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP solution build failed.' }

& (Join-Path $PSScriptRoot 'Test-Workflows.ps1')

dotnet run --project (Join-Path $root 'tests\KillerMCP.Clients.Checks\KillerMCP.Clients.Checks.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP client checks failed.' }

dotnet run --project (Join-Path $root 'tests\KillerMCP.Runtime.Checks\KillerMCP.Runtime.Checks.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP runtime checks failed.' }

dotnet run --project (Join-Path $root 'tests\KillerMCP.Setup.Checks\KillerMCP.Setup.Checks.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP setup checks failed.' }

node (Join-Path $PSScriptRoot 'Test-NativeHost.mjs')
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP native host checks failed.' }

& (Join-Path $PSScriptRoot 'Test-NativePackage.ps1') -ArtifactRoot $ArtifactRoot

if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
    & (Join-Path $PSScriptRoot 'Test-WindowsInstaller.ps1') -ArtifactRoot $ArtifactRoot
    & (Join-Path $PSScriptRoot 'Test-ClientJobUpdate.ps1') -Installer (Join-Path $ArtifactRoot 'installer\KillerMCP-Setup.exe')
    & (Join-Path $PSScriptRoot 'Test-ClientJobNotice.ps1')
    & (Join-Path $PSScriptRoot 'Test-NoticeRetry.ps1')
    & (Join-Path $PSScriptRoot 'Test-NotifyFailure.ps1') -Installer (Join-Path $ArtifactRoot 'installer\KillerMCP-Setup.exe')
}

Write-Output 'All KillerMCP checks passed.'
