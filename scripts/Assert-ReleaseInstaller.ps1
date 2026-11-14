param([Parameter(Mandatory = $true)][string]$Path)

# Chocolatey and WinGet publish the hash of whatever installer passes this check, so it has to carry
# the KillerMCP publisher's valid, timestamped Authenticode signature from the publisher's certificate
# authority, the same rule setup applies to updates.
$ErrorActionPreference = 'Stop'
$policy = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\installer\ReleaseSignature.cs'))
function Read-PublisherConstant([string]$Name) {
    $matches = [regex]::Matches($policy, ('internal const string ' + [regex]::Escape($Name) + '\s*=\s*"([^"]+)"'))
    if ($matches.Count -ne 1) { throw "The release signing policy constant is missing or ambiguous: $Name" }
    $matches[0].Groups[1].Value
}
$publisher = Read-PublisherConstant 'Publisher'
$issuerOrganization = Read-PublisherConstant 'IssuerOrganization'
$publisherPublicKeySha256 = Read-PublisherConstant 'PublisherPublicKeySha256'
$signature = Get-AuthenticodeSignature -LiteralPath $Path
if ($signature.Status -ne 'Valid') { throw "The installer signature is not valid ($($signature.Status)): $Path" }
$signer = $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
if ($signer -ne $publisher) { throw "The installer is signed by $signer, not $publisher." }
$issuer = $signature.SignerCertificate.IssuerName.Decode([Security.Cryptography.X509Certificates.X500DistinguishedNameFlags]::UseNewLines) -split '\r?\n' | Where-Object { $_ -like 'O=*' } | Select-Object -First 1
if ($issuer -ne "O=$issuerOrganization") { throw "The installer certificate was issued by $($signature.SignerCertificate.Issuer), not by $issuerOrganization." }
$algorithm = [Security.Cryptography.SHA256]::Create()
try { $publicKeyHash = [BitConverter]::ToString($algorithm.ComputeHash($signature.SignerCertificate.GetPublicKey())).Replace('-', '') }
finally { $algorithm.Dispose() }
if ($publicKeyHash -cne $publisherPublicKeySha256) { throw 'The installer signing key is not the trusted publisher key. A rekey requires a deliberate trusted pin update.' }
if (-not $signature.TimeStamperCertificate) { throw 'The installer signature has no timestamp.' }
Write-Host "Installer signed by $publisher and timestamped: $Path"
