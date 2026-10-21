using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace KillerMCP.Notify;

internal static class UpdateInstaller
{
    // The installer is far smaller than this; a larger download is refused instead of filling memory.
    internal const long MaximumInstallerBytes = 512L * 1024 * 1024;
    private const long MaximumChecksumBytes = 64 * 1024;

    /// <summary>A verified download and the SHA-256 it had when it was checked.</summary>
    internal sealed record VerifiedInstaller(string Path, string Sha256);

    /// <summary>
    /// Downloads the installer and its checksum from the same versioned release, so a release published in
    /// between cannot pair one release's installer with another's checksum, then checks the hash and the
    /// KillerMCP publisher's signature.
    /// </summary>
    internal static async Task<VerifiedInstaller> DownloadAndVerifyAsync(string version)
    {
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KillerMCP", version));
        // HttpClient.Timeout stops covering a response once its headers arrive, so one limit covers the whole download.
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        byte[] installer = await DownloadAsync(client, ReleaseFile(version, "KillerMCP-Setup.exe"), MaximumInstallerBytes, limit.Token).ConfigureAwait(false);
        string sums = System.Text.Encoding.UTF8.GetString(await DownloadAsync(client, ReleaseFile(version, "SHA256SUMS.txt"), MaximumChecksumBytes, limit.Token).ConfigureAwait(false));
        string expected = ReadExpectedHash(sums);
        string actual = Convert.ToHexString(SHA256.HashData(installer));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The update checksum does not match the published release.");

        string path = KillerMCP.Updates.DownloadedSetups.NewPath(Path.GetTempPath(), version);
        await File.WriteAllBytesAsync(path, installer).ConfigureAwait(false);
        if (!KillerMCP.Signing.ReleaseSignature.IsKillerMcpRelease(path) || !IsRelease(path, version))
        {
            File.Delete(path);
            throw new InvalidDataException("The update is not KillerMCP " + version + " signed by the KillerMCP publisher, or its signature could not be checked.");
        }
        return new VerifiedInstaller(path, actual);
    }

    /// <summary>
    /// Starts a download only if it is still the verified file. It is opened so nothing can change or replace it,
    /// and stays open while its hash, its signature and its version are checked and while it starts, so the
    /// file that starts is the file that was checked. A download can wait in the temporary folder before this.
    /// </summary>
    internal static bool TryStart(VerifiedInstaller installer, string version, string arguments)
    {
        try
        {
            using var held = Hold(installer.Path);
            if (!HashMatches(held, installer.Sha256) || !IsRelease(installer.Path, version) || !KillerMCP.Signing.ReleaseSignature.IsKillerMcpRelease(installer.Path)) return false;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installer.Path, arguments) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Opens a file for reading while refusing anyone who would write, replace or delete it.</summary>
    internal static FileStream Hold(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    internal static bool HashMatches(FileStream held, string sha256)
    {
        held.Position = 0;
        return Convert.ToHexString(SHA256.HashData(held)).Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the file is KillerMCP setup for exactly this version, so an older signed setup put in its
    /// place cannot start a downgrade.</summary>
    internal static bool IsRelease(string path, string version)
    {
        var information = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
        return string.Equals(information.ProductName, "KillerMCP-Setup", StringComparison.Ordinal)
            && Version.TryParse(information.FileVersion, out var actual) && Version.TryParse(version, out var expected)
            && actual.Major == expected.Major && actual.Minor == expected.Minor && actual.Build == expected.Build;
    }

    internal static string ReleaseFile(string version, string name)
    {
        if (!Regex.IsMatch(version, @"\A[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}\z", RegexOptions.CultureInvariant)) throw new InvalidDataException("The update version is not a release number.");
        return $"https://github.com/SteveTheKiller/KillerMCP/releases/download/v{version}/{name}";
    }

    internal static async Task<byte[]> ReadLimitedAsync(Stream source, long limit, CancellationToken token = default)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit) throw new InvalidDataException("The update download is larger than expected.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static async Task<byte[]> DownloadAsync(HttpClient client, string url, long limit, CancellationToken token)
    {
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("The update download is larger than expected.");
            using var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            return await ReadLimitedAsync(body, limit, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw new TimeoutException("The update download took longer than three minutes.");
        }
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
