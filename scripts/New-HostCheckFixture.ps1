function New-HostCheckFixture([string]$Executable, [string]$Destination) {
    if (Test-Path -LiteralPath $Destination) { throw 'Host fixture destination must be new.' }
    $source = Split-Path -Parent ([IO.Path]::GetFullPath($Executable))
    if ((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Host fixture source must not be linked.' }
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $source -Force) {
        if ($item.Name -eq 'KillerMCP.Notify.exe') { continue }
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Host fixture files must not be linked.' }
        Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse
    }
    $copied = Join-Path $Destination (Split-Path -Leaf $Executable)
    if ((Get-FileHash -LiteralPath $copied).Hash -ne (Get-FileHash -LiteralPath $Executable).Hash) { throw 'Copied host fixture differs.' }
    $copied
}
