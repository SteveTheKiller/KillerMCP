$ErrorActionPreference = 'Stop'
$version  = $env:ChocolateyPackageVersion
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $toolsDir 'Invoke-KillerMcpSetup.ps1')

# The setup copy stays in the package folder, checked against the same hash, so uninstall runs this
# verified file instead of a path read from the registry.
$setup = Join-Path $toolsDir 'KillerMCP-Setup.exe'
$fileArgs = @{
    PackageName    = $env:ChocolateyPackageName
    FileFullPath   = $setup
    Url64bit       = "https://github.com/SteveTheKiller/KillerMCP/releases/download/v$version/KillerMCP-Setup.exe"
    Checksum64     = 'REPLACE_HASH'
    ChecksumType64 = 'sha256'
}
Get-ChocolateyWebFile @fileArgs | Out-Null

# Setup runs as the signed-in account's standard token, even when Chocolatey itself is elevated.
$exitCode = Invoke-KillerMcpSetup -Setup $setup -Arguments '/silent'
if ($exitCode -eq 10) { throw 'KillerMCP setup needs the .NET 10 Desktop Runtime. Install the dotnet-10.0-desktopruntime package and try again.' }
if ($exitCode -ne 0) { throw "KillerMCP setup failed with exit code $exitCode." }
