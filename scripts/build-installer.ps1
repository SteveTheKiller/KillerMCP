param(
    [string]$PortableDirectory = (Join-Path $PSScriptRoot '..\dist\portable')
)

$ErrorActionPreference = 'Stop'
$portable = (Resolve-Path -LiteralPath $PortableDirectory).Path
$manifest = Join-Path $portable 'manifest.json'
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
    throw 'Build the KillerMCP portable package first.'
}

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
node (Join-Path $PSScriptRoot 'installer-smoke.mjs') $portable
if ($LASTEXITCODE -ne 0) { throw 'The portable runtime failed its MCP check.' }
$output = Join-Path $root 'artifacts\installer'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$payload = Join-Path $output 'payload.zip'
Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $payload -CompressionLevel Optimal -Force

$project = Join-Path $root 'installer\KillerMCP.Setup.csproj'
dotnet build $project -c Release "-p:PayloadZip=$payload" -o $output -v:q
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP installer build failed.' }
Write-Output (Join-Path $output 'KillerMCP-Setup.exe')
