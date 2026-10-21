using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace KillerMCP;

/// <summary>
/// Agent clients such as Codex run each MCP host inside a job object that ends every process in it
/// when the client tears the job down. A window the host opens, such as the update notice, has to
/// start outside that job or it closes as soon as the client closes the host.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class DetachedProcess
{
    private const uint CreateBreakawayFromJob = 0x01000000;
    private const int ErrorAccessDenied = 5;

    /// <summary>Starts a program the way the shell would, outside any job this host runs in.</summary>
    public static void Start(string path, string arguments)
    {
        if (!IsInJob())
        {
            Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = true })?.Dispose();
            return;
        }
        if (StartWithBreakaway(path, arguments)) return;
        StartThroughWmi(path, arguments);
    }

    private static bool IsInJob()
    {
        using Process current = Process.GetCurrentProcess();
        return IsProcessInJob(current.Handle, IntPtr.Zero, out bool inJob) && inJob;
    }

    // Works when the client's job allows breakaway.
    private static bool StartWithBreakaway(string path, string arguments)
    {
        var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
        var commandLine = new StringBuilder(Quote(path) + " " + arguments);
        if (!CreateProcess(path, commandLine, IntPtr.Zero, IntPtr.Zero, false, CreateBreakawayFromJob, IntPtr.Zero, Path.GetDirectoryName(path), ref startup, out ProcessInformation information))
        {
            if (Marshal.GetLastWin32Error() == ErrorAccessDenied) return false;
            throw new System.ComponentModel.Win32Exception();
        }
        CloseHandle(information.Thread);
        CloseHandle(information.Process);
        return true;
    }

    // When the job refuses breakaway, the WMI service starts the program instead. Its process
    // belongs to no client job, so closing the client cannot end it.
    private static void StartThroughWmi(string path, string arguments)
    {
        Type locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator") ?? throw new InvalidOperationException("Windows Management Instrumentation is unavailable.");
        dynamic locator = Activator.CreateInstance(locatorType)!;
        dynamic service = locator.ConnectServer(".", @"root\cimv2");
        dynamic processes = service.Get("Win32_Process");
        dynamic startup = service.Get("Win32_ProcessStartup").SpawnInstance_();
        startup.Properties_.Item("EnvironmentVariables").Value = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .Select(item => (object)(item.Key + "=" + item.Value)).ToArray();
        dynamic input = processes.Methods_.Item("Create").InParameters.SpawnInstance_();
        input.Properties_.Item("CommandLine").Value = Quote(path) + " " + arguments;
        input.Properties_.Item("CurrentDirectory").Value = Path.GetDirectoryName(path);
        input.Properties_.Item("ProcessStartupInformation").Value = startup;
        dynamic output = processes.ExecMethod_("Create", input);
        int result = Convert.ToInt32(output.Properties_.Item("ReturnValue").Value);
        if (result != 0) throw new InvalidOperationException($"The program could not start outside the agent client (WMI result {result}).");
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string? directory, ref StartupInfo startup, out ProcessInformation information);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
