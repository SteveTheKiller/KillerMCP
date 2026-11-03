param(
    [string]$ArtifactRoot = '',
    # Builds KillerMCP-Store.exe, the Microsoft Store setup whose payload carries its own .NET runtime,
    # instead of the regular KillerMCP-Setup.exe that uses the installed .NET 10 runtime.
    [switch]$Store
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$ArtifactRoot = & (Join-Path $PSScriptRoot 'Get-ArtifactRoot.ps1') -ArtifactRoot $ArtifactRoot
& (Join-Path $PSScriptRoot 'Build-NativePackage.ps1') -RuntimeIdentifier win-x64 -ArtifactRoot $ArtifactRoot -SelfContained:$Store
$package = if ($Store) { Join-Path $ArtifactRoot 'packages\win-x64-store\KillerMCP-win-x64.zip' } else { Join-Path $ArtifactRoot 'packages\win-x64\KillerMCP-win-x64.zip' }
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw 'The verified native Windows package is missing.' }

$output = Join-Path $ArtifactRoot 'installer'
New-Item -ItemType Directory -Path $output -Force | Out-Null
# Both setups come from one project, so each build starts clean instead of reusing the other's compile.
if ($Store) {
    $storeOutput = Join-Path $ArtifactRoot 'installer-store'
    dotnet build (Join-Path $root 'installer\KillerMCP.Setup.csproj') -c Release --no-incremental "-p:PayloadZip=$package" -p:StorePackage=true -o $storeOutput -v:q
    if ($LASTEXITCODE -ne 0) { throw 'KillerMCP Store installer build failed.' }
    $storeSetup = Join-Path $output 'KillerMCP-Store.exe'
    Copy-Item -LiteralPath (Join-Path $storeOutput 'KillerMCP-Setup.exe') -Destination $storeSetup -Force
    Write-Output $storeSetup
} else {
    dotnet build (Join-Path $root 'installer\KillerMCP.Setup.csproj') -c Release --no-incremental "-p:PayloadZip=$package" -o $output -v:q
    if ($LASTEXITCODE -ne 0) { throw 'KillerMCP installer build failed.' }
    Write-Output (Join-Path $output 'KillerMCP-Setup.exe')
}
