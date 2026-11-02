param([Parameter(Mandatory = $true)][string]$Installer)

$ErrorActionPreference = 'Stop'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('killermcp-root-containment-' + [guid]::NewGuid().ToString('N'))
$installed = Join-Path $scratch 'Installed'
$outside = Join-Path $scratch 'outside'
$linked = $installed + '.previous-fixture'
$previousRoot = $env:KILLERMCP_TEST_INSTALL_ROOT
try {
    [IO.Directory]::CreateDirectory($outside) | Out-Null
    $files = @{}
    foreach ($name in @('node.exe', 'killermcp.mjs', 'killertools.mjs')) {
        $path = Join-Path $outside $name
        [IO.File]::WriteAllText($path, 'outside fixture')
        $files[$name] = @{ bytes = (Get-Item -LiteralPath $path).Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    }
    $manifest = Join-Path $outside 'manifest.json'
    [IO.File]::WriteAllText($manifest, (@{ files = $files } | ConvertTo-Json -Depth 5))
    New-Item -ItemType Junction -Path $linked -Target $outside | Out-Null
    $env:KILLERMCP_TEST_INSTALL_ROOT = $installed
    $run = Start-Process -FilePath ([IO.Path]::GetFullPath($Installer)) -ArgumentList '/silent', '/uninstall' -Wait -PassThru -WindowStyle Hidden
    if ($run.ExitCode -ne 0) { throw "Isolated uninstall failed: $($run.ExitCode)" }
    foreach ($name in @('node.exe', 'killermcp.mjs', 'killertools.mjs', 'manifest.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $outside $name) -PathType Leaf)) { throw "Cleanup followed the root junction and removed $name." }
    }
    Write-Output 'PASS interrupted-removal root junction preserves every outside fixture file (1 selected regression).'
}
finally {
    $env:KILLERMCP_TEST_INSTALL_ROOT = $previousRoot
    if (Test-Path -LiteralPath $linked) { [IO.Directory]::Delete($linked) }
    $resolved = [IO.Path]::GetFullPath($scratch)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside the test temporary root.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
