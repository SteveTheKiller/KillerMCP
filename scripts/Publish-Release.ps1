param(
    [Parameter(Mandatory = $true)][string]$Repository,
    [Parameter(Mandatory = $true)][string]$Tag,
    [Parameter(Mandatory = $true)][string[]]$Assets,
    [Parameter(Mandatory = $true)][string]$NotesFile
)

# Creates and pushes the release tag, then the GitHub release. When the release cannot be created, the
# tag is removed again from origin and locally, so the release can run again without manual cleanup.
# git.exe is named in full so a git function in the calling session cannot stand in for it.
$ErrorActionPreference = 'Stop'
git.exe -C $Repository tag -a $Tag -m "KillerMCP $Tag"
if ($LASTEXITCODE -ne 0) { throw "Could not create tag $Tag." }
git.exe -C $Repository push origin $Tag
if ($LASTEXITCODE -ne 0) {
    git.exe -C $Repository tag -d $Tag | Out-Null
    throw "Could not push tag $Tag. The local tag was removed."
}
Push-Location -LiteralPath $Repository
try { gh release create $Tag @Assets --title "KillerMCP $Tag" --notes-file $NotesFile --verify-tag }
finally { Pop-Location }
if ($LASTEXITCODE -ne 0) {
    git.exe -C $Repository push origin ":refs/tags/$Tag"
    $remoteRemoved = $LASTEXITCODE -eq 0
    git.exe -C $Repository tag -d $Tag | Out-Null
    $draft = "If GitHub kept a draft release for $Tag, delete it with: gh release delete $Tag"
    if ($remoteRemoved) { throw "GitHub release creation failed. Tag $Tag was removed from origin and locally, so the release can run again. $draft" }
    throw "GitHub release creation failed, and tag $Tag could not be removed from origin. Remove it with: git push origin :refs/tags/$Tag. $draft"
}
