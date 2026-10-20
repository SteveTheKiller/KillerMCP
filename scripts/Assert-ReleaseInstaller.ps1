param([Parameter(Mandatory = $true)][string]$Path)

# Chocolatey and WinGet publish the hash of whatever installer passes this check, so it has to carry
# the KillerMCP publisher's valid, timestamped Authenticode signature, the same rule setup applies to updates.
$ErrorActionPreference = 'Stop'
$publisher = 'Open Source Developer Stephen Riley'
$signature = Get-AuthenticodeSignature -LiteralPath $Path
if ($signature.Status -ne 'Valid') { throw "The installer signature is not valid ($($signature.Status)): $Path" }
$signer = $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
if ($signer -ne $publisher) { throw "The installer is signed by $signer, not $publisher." }
if (-not $signature.TimeStamperCertificate) { throw 'The installer signature has no timestamp.' }
Write-Host "Installer signed by $publisher and timestamped: $Path"
