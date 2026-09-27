$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

dotnet build (Join-Path $root 'KillerMCP.slnx') -c Release
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP solution build failed.' }

dotnet run --project (Join-Path $root 'tests\KillerMCP.Clients.Checks\KillerMCP.Clients.Checks.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP client checks failed.' }

dotnet run --project (Join-Path $root 'tests\KillerMCP.Runtime.Checks\KillerMCP.Runtime.Checks.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP runtime checks failed.' }

node (Join-Path $PSScriptRoot 'Test-NativeHost.mjs')
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP native host checks failed.' }

& (Join-Path $PSScriptRoot 'Test-NativePackage.ps1')

if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
    & (Join-Path $PSScriptRoot 'Test-WindowsInstaller.ps1')
}

Write-Output 'All KillerMCP checks passed.'
