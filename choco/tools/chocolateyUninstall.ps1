$ErrorActionPreference = 'Stop'

# KillerMCP installs for the current user, so its uninstall entry lives under HKCU.
$entry = Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\KillerMCP' -ErrorAction SilentlyContinue
if (-not $entry) {
    Write-Warning "KillerMCP has no uninstall entry for $env:USERNAME, so nothing was removed. If another account installed it, uninstall it from that account."
    return
}
$setup = if ($entry.InstallLocation) { $entry.InstallLocation + '-Setup.exe' } else { $null }
if (-not $setup -or -not (Test-Path -LiteralPath $setup -PathType Leaf)) {
    # Reporting success here would leave KillerMCP and its agent client connections in place.
    throw "KillerMCP is installed, but its setup copy is missing ($setup). Download KillerMCP-Setup.exe from https://github.com/SteveTheKiller/KillerMCP/releases and run it with /uninstall."
}
$process = Start-Process -FilePath $setup -ArgumentList '/silent', '/uninstall' -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "KillerMCP uninstall failed with exit code $($process.ExitCode)." }
