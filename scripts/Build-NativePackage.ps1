param(
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$SkipTest
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = Join-Path $root "artifacts\packages\$RuntimeIdentifier"
$staging = Join-Path $root "artifacts\staging\$RuntimeIdentifier"

if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}
New-Item -ItemType Directory -Path $staging -Force | Out-Null

dotnet publish (Join-Path $root 'src\KillerMCP\KillerMCP.csproj') -c Release -r $RuntimeIdentifier --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $staging
if ($LASTEXITCODE -ne 0) { throw 'Native KillerMCP publish failed.' }
dotnet publish (Join-Path $root 'src\KillerMCP.Configure\KillerMCP.Configure.csproj') -c Release -r $RuntimeIdentifier --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $staging
if ($LASTEXITCODE -ne 0) { throw 'Native KillerMCP configurator publish failed.' }

$files = [ordered]@{}
Get-ChildItem -LiteralPath $staging -File -Recurse | Sort-Object FullName | ForEach-Object {
    $name = $_.FullName.Substring($staging.Length).TrimStart([char[]]@('\', '/')).Replace('\', '/')
    $files[$name] = [ordered]@{
        bytes = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
}
if (-not $files.Contains('KillerMCP.exe') -and -not $files.Contains('KillerMCP')) {
    throw 'The native package does not contain the KillerMCP executable.'
}
if (-not $files.Contains('KillerMCP.Configure.exe') -and -not $files.Contains('KillerMCP.Configure')) {
    throw 'The native package does not contain the KillerMCP configurator.'
}

$manifest = [ordered]@{
    formatVersion = 1
    runtimeIdentifier = $RuntimeIdentifier
    framework = 'net10.0'
    selfContained = $false
    files = $files
}
$manifestJson = $manifest | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $staging 'manifest.json'), $manifestJson, (New-Object Text.UTF8Encoding($false)))

if (-not $SkipTest) {
    $runtime = [System.Runtime.InteropServices.RuntimeInformation]
    $current = if ($runtime::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) { 'win-' } elseif ($runtime::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) { 'linux-' } elseif ($runtime::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) { 'osx-' } else { '' }
    if (-not $RuntimeIdentifier.StartsWith($current, [StringComparison]::Ordinal)) {
        throw "Cannot execute a $RuntimeIdentifier package on this operating system. Use -SkipTest only for cross-platform packaging."
    }
    node (Join-Path $PSScriptRoot 'Test-NativePackage.mjs') $staging
    if ($LASTEXITCODE -ne 0) { throw 'The native package failed its MCP check.' }
}

New-Item -ItemType Directory -Path $output -Force | Out-Null
$archive = Join-Path $output "KillerMCP-$RuntimeIdentifier.zip"
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $archive -CompressionLevel Optimal -Force
Write-Output $archive
