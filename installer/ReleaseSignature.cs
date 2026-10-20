using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace KillerMCP.Signing
{
    /// <summary>
    /// Decides whether a setup file is a KillerMCP release: Windows trusts its Authenticode signature,
    /// no certificate in the chain is revoked, and the signer is the KillerMCP publisher. Setup and the
    /// update window both compile this file, so both paths apply the same rule. The publisher is pinned
    /// by subject rather than thumbprint so a renewed certificate keeps working.
    /// </summary>
    internal static class ReleaseSignature
    {
        internal const string Publisher = "Open Source Developer Stephen Riley";

        internal static bool IsKillerMcpRelease(string path) => HasTrustedSignature(path) && SignedBy(path) == Publisher;

        /// <summary>The signer's common name, or null when the file has no readable signature.</summary>
        internal static string? SignedBy(string path)
        {
            try
            {
                // .NET has no other way to read an Authenticode signer. This only reads the name;
                // WinVerifyTrust is what decides whether the signature is trusted.
#pragma warning disable SYSLIB0057
                using (var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
#pragma warning restore SYSLIB0057
                    return certificate.GetNameInfo(X509NameType.SimpleName, false);
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        // Revocation is checked for the whole chain. When the check cannot finish, for example offline,
        // the signature is not trusted and the update waits for a later check.
        internal static bool HasTrustedSignature(string path)
        {
            var file = new WinTrustFileInfo(path);
            IntPtr filePointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
            IntPtr dataPointer = IntPtr.Zero;
            IntPtr actionPointer = IntPtr.Zero;
            try
            {
                Marshal.StructureToPtr(file, filePointer, false);
                dataPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustData)));
                Marshal.StructureToPtr(new WinTrustData(filePointer), dataPointer, false);
                actionPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Guid)));
                Marshal.StructureToPtr(new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"), actionPointer, false);
                return WinVerifyTrust(IntPtr.Zero, actionPointer, dataPointer) == 0;
            }
            finally
            {
                if (actionPointer != IntPtr.Zero) Marshal.FreeHGlobal(actionPointer);
                if (dataPointer != IntPtr.Zero) Marshal.FreeHGlobal(dataPointer);
                Marshal.DestroyStructure(filePointer, typeof(WinTrustFileInfo));
                Marshal.FreeHGlobal(filePointer);
            }
        }

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern int WinVerifyTrust(IntPtr hwnd, IntPtr actionId, IntPtr trustData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint Size;
            [MarshalAs(UnmanagedType.LPWStr)] public string Path;
            public IntPtr Handle;
            public IntPtr Subject;

            public WinTrustFileInfo(string path)
            {
                Size = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo));
                Path = path;
                Handle = IntPtr.Zero;
                Subject = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            private const uint NoUi = 2;
            private const uint RevokeWholeChain = 1;
            private const uint FileChoice = 1;
            private const uint RevocationCheckChain = 0x00000040;

            public uint Size;
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

            public WinTrustData(IntPtr file)
            {
                Size = (uint)Marshal.SizeOf(typeof(WinTrustData));
                PolicyCallbackData = IntPtr.Zero;
                SipClientData = IntPtr.Zero;
                UiChoice = NoUi;
                RevocationChecks = RevokeWholeChain;
                UnionChoice = FileChoice;
                FileInfo = file;
                StateAction = 0;
                StateData = IntPtr.Zero;
                UrlReference = IntPtr.Zero;
                ProviderFlags = RevocationCheckChain;
                UiContext = 0;
            }
        }
    }
}
