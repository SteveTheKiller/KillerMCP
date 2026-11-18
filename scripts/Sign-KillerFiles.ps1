param(
    [Parameter(Mandatory = $true)][string]$SignTool,
    [string]$CertThumbprint = '',
    [string]$CertName = '',
    [Parameter(Mandatory = $true)][string[]]$Path
)

# Signs KillerMCP programs with the release certificate, tries each timestamp server in turn, and accepts a
# file only once it verifies and carries the KillerMCP publisher's pinned, timestamped signature.
$ErrorActionPreference = 'Stop'
if (-not $CertThumbprint -and -not $CertName) { throw 'Name the signing certificate by thumbprint or subject.' }
$certArgs = if ($CertThumbprint) { @('/sha1', $CertThumbprint) } else { @('/n', $CertName) }
$assert = Join-Path $PSScriptRoot 'Assert-ReleaseInstaller.ps1'
foreach ($file in $Path) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "The file to sign is missing: $file" }
    $signed = $false
    foreach ($timestampServer in @('http://timestamp.digicert.com', 'http://timestamp.sectigo.com', 'http://ts.ssl.com')) {
        & $SignTool sign /fd sha256 /tr $timestampServer /td sha256 @certArgs /d 'KillerMCP' /du 'https://github.com/SteveTheKiller/KillerMCP' /v $file
        if ($LASTEXITCODE -eq 0) { $signed = $true; break }
        Start-Sleep -Seconds 3
    }
    if (-not $signed) { throw "Signing $(Split-Path -Leaf $file) failed with every timestamp server." }
    & $SignTool verify /pa /v $file
    if ($LASTEXITCODE -ne 0) { throw "The signed $(Split-Path -Leaf $file) failed Authenticode verification." }
    & $assert -Path $file
}
