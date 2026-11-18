using System.Runtime.InteropServices;

// Setup and the notifier run from Downloads and the temporary folder, where anything can sit beside them.
// Native libraries they load by name therefore come only from System32, never from their own folder.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace KillerMCP.Signing
{
    internal static class NativeLoadSafety
    {
        private const uint LoadLibrarySearchSystem32 = 0x00000800;

        /// <summary>Limits every later LoadLibrary by name to System32. Call it first thing at startup.</summary>
        internal static void Apply() => SetDefaultDllDirectories(LoadLibrarySearchSystem32);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDefaultDllDirectories(uint flags);
    }
}
