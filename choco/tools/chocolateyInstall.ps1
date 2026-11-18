$ErrorActionPreference = 'Stop'
$version  = $env:ChocolateyPackageVersion
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

# KillerMCP installs for one Windows account and its setup refuses to run as administrator, because an
# elevated install could write agent client settings through links a standard user planted. Chocolatey
# runs elevated by default, so say how to install it instead of failing with a bare exit code.
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if ($identity.IsSystem -or ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'KillerMCP installs for your own Windows account and refuses to install as administrator. Run this from a normal, non-elevated shell with Chocolatey configured for non-admin installs, or install it with KillerMCP-Setup.exe from https://github.com/SteveTheKiller/KillerMCP/releases.'
}

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
Install-ChocolateyInstallPackage -PackageName $env:ChocolateyPackageName -FileType 'exe' -SilentArgs '/silent' -File64 $setup -ValidExitCodes @(0)
