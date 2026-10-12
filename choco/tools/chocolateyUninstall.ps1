$ErrorActionPreference = 'Stop'

# KillerMCP installs for the current user, so its uninstall entry lives under HKCU.
$entry = Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\KillerMCP' -ErrorAction SilentlyContinue
$setup = if ($entry -and $entry.InstallLocation) { $entry.InstallLocation + '-Setup.exe' } else { $null }
if ($setup -and (Test-Path -LiteralPath $setup -PathType Leaf)) {
    $process = Start-Process -FilePath $setup -ArgumentList '/silent', '/uninstall' -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "KillerMCP uninstall failed with exit code $($process.ExitCode)." }
}
