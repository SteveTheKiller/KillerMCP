#if NET8_0_OR_GREATER
using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

internal static class InstallerSignerReviewChecks
{
    internal static void Run(string? signedSetupPath = null)
    {
        using var issuerKey = RSA.Create(2048);
        var issuerRequest = new CertificateRequest("CN=Certum Code Signing 2021 CA, O=Asseco Data Systems S.A., C=PL", issuerKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        issuerRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var issuer = issuerRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var otherKey = RSA.Create(2048);
        var request = new CertificateRequest("CN=Open Source Developer Stephen Riley, O=Open Source Developer, C=US", otherKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var impostor = request.Create(issuer, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1), new byte[] { 1, 2, 3 });
        if (KillerMCP.Signing.ReleaseSignature.IsPublisherCertificate(impostor))
            throw new InvalidOperationException("A different key with the same publisher and issuer was accepted.");
        string setup = signedSetupPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "KillerMCP-Setup.exe");
#pragma warning disable SYSLIB0057
        using var installed = Path.GetExtension(setup).Equals(".cer", StringComparison.OrdinalIgnoreCase)
            ? new X509Certificate2(setup)
            : new X509Certificate2(X509Certificate.CreateFromSignedFile(setup));
#pragma warning restore SYSLIB0057
        if (!KillerMCP.Signing.ReleaseSignature.IsPublisherCertificate(installed))
            throw new InvalidOperationException("The installed release publisher was refused.");
        var renewalRequest = new CertificateRequest(new X500DistinguishedName(installed.Subject), installed.PublicKey, HashAlgorithmName.SHA256);
        using var renewal = renewalRequest.Create(issuer.SubjectName, X509SignatureGenerator.CreateForRSA(issuerKey, RSASignaturePadding.Pkcs1), DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1), new byte[] { 3, 2, 1 });
        if (!KillerMCP.Signing.ReleaseSignature.IsPublisherCertificate(renewal))
            throw new InvalidOperationException("A same-key renewal was refused by the publisher key pin.");
        Console.WriteLine("PASS publisher pin rejects rekey, accepts pinned signer and same-key renewal (3 selected regressions)");
    }
}
#endif
