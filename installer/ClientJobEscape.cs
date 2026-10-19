using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace KillerMCP.Setup
{
    /// <summary>
    /// Agent clients such as Codex run each MCP host inside a job object that ends every process in
    /// it when the client tears the job down. An update window opened by such a host starts setup in
    /// that job, and setup has to close the hosts to replace them, so the client kills setup partway
    /// through the upgrade. Setup therefore reruns itself outside the job before it installs.
    /// </summary>
    internal static class ClientJobEscape
    {
        internal const string DetachedArgument = "/detached";
        private const uint CreateBreakawayFromJob = 0x01000000;
        private const int ErrorAccessDenied = 5;

        /// <summary>Returns the exit code of the copy that ran outside the job, or null when this
        /// process should install by itself.</summary>
        internal static int? RunOutsideClientJob(string[] args)
        {
            if (args.Any(value => value.Equals(DetachedArgument, StringComparison.OrdinalIgnoreCase))) return null;
            if (!IsInJob()) return null;
            string setup = Assembly.GetExecutingAssembly().Location;
            string arguments = string.Join(" ", args.Select(Argument).Concat(new[] { DetachedArgument }));
            return StartWithBreakaway(setup, arguments) ?? StartThroughWmi(setup, arguments);
        }

        private static bool IsInJob()
        {
            using (Process current = Process.GetCurrentProcess())
                return IsProcessInJob(current.Handle, IntPtr.Zero, out bool inJob) && inJob;
        }

        // Works when the client's job allows breakaway.
        private static int? StartWithBreakaway(string setup, string arguments)
        {
            var startup = new StartupInfo { cb = Marshal.SizeOf(typeof(StartupInfo)) };
            var commandLine = new StringBuilder(Argument(setup) + " " + arguments);
            if (!CreateProcess(setup, commandLine, IntPtr.Zero, IntPtr.Zero, false, CreateBreakawayFromJob, IntPtr.Zero, Environment.CurrentDirectory, ref startup, out ProcessInformation information))
            {
                if (Marshal.GetLastWin32Error() == ErrorAccessDenied) return null;
                throw new System.ComponentModel.Win32Exception();
            }
            try
            {
                WaitForSingleObject(information.Process, uint.MaxValue);
                if (!GetExitCodeProcess(information.Process, out uint exitCode)) throw new System.ComponentModel.Win32Exception();
                return unchecked((int)exitCode);
            }
            finally
            {
                CloseHandle(information.Thread);
                CloseHandle(information.Process);
            }
        }

        // When the job refuses breakaway, the WMI service starts setup instead. Its process belongs
        // to no client job, so closing the client cannot end it.
        private static int StartThroughWmi(string setup, string arguments)
        {
            using (var startup = new ManagementClass("Win32_ProcessStartup").CreateInstance())
            using (var processes = new ManagementClass("Win32_Process"))
            using (ManagementBaseObject input = processes.GetMethodParameters("Create"))
            {
                startup["EnvironmentVariables"] = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
                    .Select(item => item.Key + "=" + item.Value).ToArray();
                input["CommandLine"] = Argument(setup) + " " + arguments;
                input["CurrentDirectory"] = Environment.CurrentDirectory;
                input["ProcessStartupInformation"] = startup;
                using (ManagementBaseObject output = processes.InvokeMethod("Create", input, null))
                {
                    uint result = Convert.ToUInt32(output["ReturnValue"]);
                    if (result != 0) throw new InvalidOperationException("Setup could not start outside the agent client (WMI result " + result + ").");
                    using (Process detached = Process.GetProcessById(Convert.ToInt32(output["ProcessId"])))
                    {
                        // Opening the handle now keeps the exit code readable after setup finishes.
                        _ = detached.Handle;
                        detached.WaitForExit();
                        return detached.ExitCode;
                    }
                }
            }
        }

        // Quotes an argument the way the command line parser reads it back.
        private static string Argument(string value)
        {
            if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return value;
            var quoted = new StringBuilder("\"");
            int slashes = 0;
            foreach (char character in value)
            {
                if (character == '\\') { slashes++; continue; }
                quoted.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
                slashes = 0;
                quoted.Append(character);
            }
            quoted.Append('\\', slashes * 2);
            return quoted.Append('"').ToString();
        }

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
        private static extern bool CreateProcess(string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation information);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
