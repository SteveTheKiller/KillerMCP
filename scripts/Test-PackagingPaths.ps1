param([Parameter(Mandatory)][string]$ScratchRoot)

# Exercise real package and installer orchestration with the compiler and runtime
# at their process boundaries. No product executable, installer or signer runs.
$ErrorActionPreference = 'Stop'
$fixture = Join-Path ([IO.Path]::GetFullPath($ScratchRoot)) ('packaging-paths-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
function dotnet {
    $arguments = @($args)
    $output = $arguments[[Array]::IndexOf($arguments, '-o') + 1]
    if (-not $output.StartsWith($fixture + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Build escaped the assigned output root: $output"
    }
    [IO.Directory]::CreateDirectory($output) | Out-Null
    if ($arguments[0] -eq 'publish') {
        $name = switch -Wildcard ($arguments[1]) {
            '*Configure*' { 'KillerMCP.Configure.exe' }
            '*Notify*' { 'KillerMCP.Notify.exe' }
            default { 'KillerMCP.exe' }
        }
    } else { $name = 'KillerMCP-Setup.exe' }
    [IO.File]::WriteAllText((Join-Path $output $name), 'fixture payload')
    $global:LASTEXITCODE = 0
}
function node { $global:LASTEXITCODE = 0 }

& (Join-Path $PSScriptRoot 'Build-WindowsInstaller.ps1') -ArtifactRoot $fixture | Out-Null
$package = Join-Path $fixture 'packages/win-x64/KillerMCP-win-x64.zip'
$installer = Join-Path $fixture 'installer/KillerMCP-Setup.exe'
if (-not (Test-Path -LiteralPath $package) -or -not (Test-Path -LiteralPath $installer)) {
    throw 'The package and installer did not share the requested scratch root.'
}
$manifest = Get-Content -Raw (Join-Path $fixture 'staging/win-x64/manifest.json') | ConvertFrom-Json
foreach ($file in $manifest.files.PSObject.Properties) {
    $actual = Get-FileHash -LiteralPath (Join-Path $fixture ('staging/win-x64/' + $file.Name))
    if ($actual.Hash -ne $file.Value.sha256) { throw 'Manifest hash mismatch.' }
}
$defaultRoot = & (Join-Path $PSScriptRoot 'Get-ArtifactRoot.ps1')
if ($defaultRoot -notlike '*scratch*' -or $defaultRoot.StartsWith((Split-Path $PSScriptRoot))) {
    throw 'Default packaging output is not outside the repository in scratch.'
}
try {
    & (Join-Path $PSScriptRoot 'Get-ArtifactRoot.ps1') -ArtifactRoot (Join-Path (Split-Path $PSScriptRoot) 'artifacts') | Out-Null
    throw 'Repository output was accepted.'
} catch {
    if ($_.Exception.Message -notlike '*outside a Git repository*') { throw }
}
Write-Output "PASS: package, installer, manifest and default scratch path; repository output refused. Fixture: $fixture"
