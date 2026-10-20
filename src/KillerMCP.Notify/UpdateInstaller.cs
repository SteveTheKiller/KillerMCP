using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace KillerMCP.Notify;

internal static class UpdateInstaller
{
    internal static async Task<string> DownloadAndVerifyAsync(string version, string installerUrl)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KillerMCP", version));
        byte[] installer = await client.GetByteArrayAsync(installerUrl).ConfigureAwait(false);
        string sumsUrl = $"https://github.com/SteveTheKiller/KillerMCP/releases/download/v{version}/SHA256SUMS.txt";
        string sums = await client.GetStringAsync(sumsUrl).ConfigureAwait(false);
        string expected = ReadExpectedHash(sums);
        string actual = Convert.ToHexString(SHA256.HashData(installer));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The update checksum does not match the published release.");

        string path = Path.Combine(Path.GetTempPath(), $"KillerMCP-Setup-{version}-{Guid.NewGuid():N}.exe");
        await File.WriteAllBytesAsync(path, installer).ConfigureAwait(false);
        if (!KillerMCP.Signing.ReleaseSignature.IsKillerMcpRelease(path))
        {
            File.Delete(path);
            throw new InvalidDataException("The update is not signed by the KillerMCP publisher, or its signature could not be checked.");
        }
        return path;
    }

    private static string ReadExpectedHash(string sums)
    {
        foreach (string line in sums.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts.Any(part => part.EndsWith("KillerMCP-Setup.exe", StringComparison.OrdinalIgnoreCase))) return parts[0];
        }
        throw new InvalidDataException("The release checksum is missing.");
    }

}
