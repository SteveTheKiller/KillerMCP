param([string]$ArtifactRoot = '')

# Resolve without creating output. Every caller uses the same root for packages,
# staging, installers and release checksums.
$ErrorActionPreference = 'Stop'
if (-not $ArtifactRoot) {
    $ArtifactRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'scratch/KillerMCP-packaging'
}
$resolved = [IO.Path]::GetFullPath($ArtifactRoot)
$ancestor = $resolved
while ($ancestor) {
    if (Test-Path -LiteralPath (Join-Path $ancestor '.git')) {
        throw 'Packaging output must be outside a Git repository.'
    }
    if (Test-Path -LiteralPath $ancestor) {
        if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Packaging output must not pass through a reparse point.'
        }
    }
    $ancestor = Split-Path -Parent $ancestor
}
$resolved
