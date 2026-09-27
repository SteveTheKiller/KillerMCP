# release.ps1 - KillerMCP release workflow
# Builds, signs with Certum SimplySign, verifies, tests, and optionally publishes.
# Compatible with Windows PowerShell 5.1 and PowerShell 7.
#
# Usage:
#   .\release.ps1
#   .\release.ps1 -Publish
#   .\release.ps1 -SkipSign

[CmdletBinding()]
param(
    [string]$KillerToolsRoot = (Join-Path $env:USERPROFILE 'killer-tools-site'),
    [string]$CertThumbprint = '',
    [string]$CertName = 'Open Source Developer Stephen Riley',
    [switch]$SkipSign,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

function Fail([string]$Message) {
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit 1
}

function Step([string]$Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][scriptblock]$Command,
        [Parameter(Mandatory)][string]$Failure
    )
    & $Command
    if ($LASTEXITCODE -ne 0) { Fail $Failure }
}

if ($Publish -and $SkipSign) {
    Fail 'An unsigned KillerMCP installer cannot be published.'
}

$package = Get-Content -LiteralPath 'package.json' -Raw | ConvertFrom-Json
$Version = [string]$package.version
if ($Version -notmatch '^\d+\.\d+\.\d+$') { Fail 'package.json does not contain an x.y.z version.' }
$Tag = "v$Version"
$installer = Join-Path $PSScriptRoot 'artifacts\installer\KillerMCP-Setup.exe'
$sumsFile = Join-Path $PSScriptRoot 'artifacts\installer\SHA256SUMS.txt'
$killerToolsMcp = Join-Path $KillerToolsRoot 'mcp'
$killerToolsBundle = Join-Path $killerToolsMcp 'dist\killermcp.mjs'

Step "Checking KillerMCP $Version release inputs"
if (-not (Test-Path -LiteralPath $killerToolsMcp -PathType Container)) {
    Fail "KillerTools MCP source was not found at $killerToolsMcp"
}
if ((git branch --show-current).Trim() -ne 'main') { Fail 'KillerMCP releases must run from main.' }
$dashMatches = @(git grep -n -I -P '[\x{2013}\x{2014}]' -- . 2>$null)
if ($dashMatches.Count) {
    Write-Host ($dashMatches -join "`n")
    Fail 'Prohibited Unicode dash characters were found.'
}

if ($Publish) {
    $dirty = @(git status --porcelain)
    if ($dirty.Count) { Fail "The working tree is not clean:`n$($dirty -join "`n")" }
    git fetch origin main --quiet
    if ($LASTEXITCODE -ne 0) { Fail 'Could not fetch origin/main.' }
    $local = (git rev-parse HEAD).Trim()
    $remote = (git rev-parse origin/main).Trim()
    if ($local -ne $remote) { Fail 'Local main and origin/main differ. Push the reviewed source first.' }
    if (git tag --list $Tag) { Fail "Tag $Tag already exists locally." }
    if (git ls-remote --tags origin $Tag) { Fail "Tag $Tag already exists on origin." }
}

Step 'Building and checking the KillerTools local bundle'
Push-Location $killerToolsMcp
try {
    Invoke-Checked { pnpm typecheck } 'KillerTools MCP typecheck failed.'
    Invoke-Checked { pnpm coverage } 'KillerTools MCP coverage check failed.'
    Invoke-Checked { pnpm build:local } 'KillerTools local MCP bundle failed to build.'
} finally {
    Pop-Location
}
if (-not (Test-Path -LiteralPath $killerToolsBundle -PathType Leaf)) {
    Fail 'The KillerTools local MCP bundle was not produced.'
}

Step 'Building the KillerMCP portable runtime'
Invoke-Checked { node scripts\build.mjs $killerToolsBundle } 'KillerMCP runtime staging failed.'
Invoke-Checked { node scripts\discovery-test.mjs } 'KillerMCP discovery tests failed.'
Invoke-Checked { node scripts\build-portable.mjs } 'KillerMCP portable build failed.'

Step 'Building KillerMCP Setup'
Invoke-Checked { powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-installer.ps1 } 'KillerMCP installer build failed.'
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { Fail "Installer was not produced: $installer" }

if ($SkipSign) {
    Write-Host 'WARNING: The installer is unsigned and cannot be published.' -ForegroundColor Red
} else {
    Step 'Signing KillerMCP Setup'
    if (-not (Get-Process -Name 'SimplySignDesktop' -ErrorAction SilentlyContinue)) {
        Fail 'SimplySign Desktop is not running. Start it, sign in, and run the release again.'
    }

    $signtool = (Get-Command signtool -ErrorAction SilentlyContinue).Source
    if (-not $signtool) {
        $kitBase = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
        if (Test-Path -LiteralPath $kitBase) {
            $signtool = Get-ChildItem -LiteralPath $kitBase -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
                Sort-Object FullName -Descending |
                Select-Object -First 1 -ExpandProperty FullName
        }
    }
    if (-not $signtool) { Fail 'signtool.exe was not found. Install the Windows SDK.' }
    Write-Host "signtool: $signtool"

    $certArgs = if ($CertThumbprint) { @('/sha1', $CertThumbprint) } else { @('/n', $CertName) }
    $timestampServers = @(
        'http://timestamp.digicert.com',
        'http://timestamp.sectigo.com',
        'http://ts.ssl.com'
    )
    $signed = $false
    foreach ($timestampServer in $timestampServers) {
        Write-Host "Trying timestamp server: $timestampServer"
        & $signtool sign /fd sha256 /tr $timestampServer /td sha256 @certArgs `
            /d 'KillerMCP' /du 'https://github.com/SteveTheKiller/KillerMCP' /v $installer
        if ($LASTEXITCODE -eq 0) { $signed = $true; break }
        Start-Sleep -Seconds 3
    }
    if (-not $signed) { Fail 'Signing failed with every timestamp server.' }

    & $signtool verify /pa /v $installer
    if ($LASTEXITCODE -ne 0) { Fail 'The signed installer failed Authenticode verification.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid') { Fail "Authenticode status is $($signature.Status)." }
    if (-not $signature.TimeStamperCertificate) { Fail 'The installer signature does not have a timestamp.' }
    Write-Host "Signer: $($signature.SignerCertificate.Subject)" -ForegroundColor Green
    Write-Host "Timestamp: $($signature.TimeStamperCertificate.Subject)" -ForegroundColor Green
}

Step 'Testing the exact release installer'
Invoke-Checked { powershell -NoProfile -ExecutionPolicy Bypass -File scripts\test-installer.ps1 -Installer $installer } 'The exact release installer failed its test suite.'
if (-not $SkipSign) {
    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate) {
        Fail 'The installer signature changed or became invalid during testing.'
    }
}

Step 'Writing the release checksum'
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumLine = "${hash}  KillerMCP-Setup.exe"
[IO.File]::WriteAllLines($sumsFile, @($checksumLine), [Text.Encoding]::ASCII)
Write-Host $checksumLine -ForegroundColor Green

if ($Publish) {
    Step "Publishing KillerMCP $Tag"
    git tag -a $Tag -m "KillerMCP $Tag"
    if ($LASTEXITCODE -ne 0) { Fail "Could not create tag $Tag." }
    git push origin $Tag
    if ($LASTEXITCODE -ne 0) { Fail "Could not push tag $Tag." }
    gh release create $Tag $installer $sumsFile --title "KillerMCP $Tag" --generate-notes --verify-tag
    if ($LASTEXITCODE -ne 0) { Fail 'GitHub release creation failed.' }
    gh release view $Tag --json url --jq '.url'
} else {
    Write-Host ''
    Write-Host 'Release artifacts are verified. Nothing was published.' -ForegroundColor Green
    Write-Host "Run .\release.ps1 -Publish after main is pushed and the release is approved."
}
