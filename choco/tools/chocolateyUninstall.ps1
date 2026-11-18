$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $toolsDir 'Invoke-KillerMcpSetup.ps1')

# Uninstall runs only the setup copy this package downloaded and checked, never a path from the user's
# registry entry, which any program running as that user could point somewhere else.
$setup = Join-Path $toolsDir 'KillerMCP-Setup.exe'
if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) {
    throw "The verified KillerMCP setup copy is missing from $toolsDir. Download KillerMCP-Setup.exe from https://github.com/SteveTheKiller/KillerMCP/releases and run it with /uninstall from your normal account."
}
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
if ($hash -ne 'REPLACE_HASH') { throw "The KillerMCP setup copy in $toolsDir changed after it was verified, so it was not run." }
$signature = Get-AuthenticodeSignature -LiteralPath $setup
if ($signature.Status -ne 'Valid') { throw "The KillerMCP setup copy in $toolsDir is not validly signed ($($signature.Status)), so it was not run." }

# Setup runs as the signed-in account's standard token, even when Chocolatey itself is elevated.
$exitCode = Invoke-KillerMcpSetup -Setup $setup -Arguments '/silent', '/uninstall'
if ($exitCode -ne 0) { throw "KillerMCP uninstall failed with exit code $exitCode." }
