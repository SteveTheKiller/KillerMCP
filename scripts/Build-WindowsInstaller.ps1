param([string]$ArtifactRoot = '')

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$ArtifactRoot = & (Join-Path $PSScriptRoot 'Get-ArtifactRoot.ps1') -ArtifactRoot $ArtifactRoot
& (Join-Path $PSScriptRoot 'Build-NativePackage.ps1') -RuntimeIdentifier win-x64 -ArtifactRoot $ArtifactRoot
$package = Join-Path $ArtifactRoot 'packages\win-x64\KillerMCP-win-x64.zip'
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw 'The verified native Windows package is missing.' }

$output = Join-Path $ArtifactRoot 'installer'
New-Item -ItemType Directory -Path $output -Force | Out-Null
dotnet build (Join-Path $root 'installer\KillerMCP.Setup.csproj') -c Release "-p:PayloadZip=$package" -o $output -v:q
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP installer build failed.' }
Write-Output (Join-Path $output 'KillerMCP-Setup.exe')
