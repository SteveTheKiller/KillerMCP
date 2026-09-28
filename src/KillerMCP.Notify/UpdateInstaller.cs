using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
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
        if (!HasTrustedSignature(path))
        {
            File.Delete(path);
            throw new InvalidDataException("The update does not have a trusted Authenticode signature.");
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

    private static bool HasTrustedSignature(string path)
    {
        var file = new WinTrustFileInfo(path);
        IntPtr filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        IntPtr dataPointer = IntPtr.Zero;
        IntPtr actionPointer = IntPtr.Zero;
        try
        {
            Marshal.StructureToPtr(file, filePointer, false);
            var data = new WinTrustData(filePointer);
            dataPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(data, dataPointer, false);
            actionPointer = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
            Marshal.StructureToPtr(WinTrustActionGenericVerifyV2, actionPointer, false);
            return WinVerifyTrust(IntPtr.Zero, actionPointer, dataPointer) == 0;
        }
        finally
        {
            if (actionPointer != IntPtr.Zero) Marshal.FreeHGlobal(actionPointer);
            if (dataPointer != IntPtr.Zero) Marshal.FreeHGlobal(dataPointer);
            Marshal.DestroyStructure<WinTrustFileInfo>(filePointer);
            Marshal.FreeHGlobal(filePointer);
        }
    }

    private static readonly Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, IntPtr actionId, IntPtr trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;

        public WinTrustFileInfo(string path)
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            FilePath = path;
            FileHandle = IntPtr.Zero;
            KnownSubject = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;

        public WinTrustData(IntPtr fileInfo)
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustData>();
            PolicyCallbackData = IntPtr.Zero;
            SipClientData = IntPtr.Zero;
            UiChoice = 2;
            RevocationChecks = 0;
            UnionChoice = 1;
            FileInfo = fileInfo;
            StateAction = 0;
            StateData = IntPtr.Zero;
            UrlReference = IntPtr.Zero;
            ProviderFlags = 0x00000010;
            UiContext = 0;
        }
    }
}
