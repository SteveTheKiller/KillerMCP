$ErrorActionPreference = 'Stop'
$installPath = Join-Path $env:LOCALAPPDATA 'Programs\KillerMCP\KillerMCP.exe'
if (-not (Test-Path -LiteralPath $installPath -PathType Leaf)) {
    [Console]::Error.WriteLine('KillerMCP is not installed for this Windows user. Install KillerMCP before enabling the Claude plugin.')
    exit 1
}
& $installPath
exit $LASTEXITCODE
