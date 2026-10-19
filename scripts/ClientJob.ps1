# Shared by the client job tests: a job object shaped like the ones agent clients such as Codex
# put their KillerMCP hosts in. Closing it ends every process still inside.
if (-not ('KillerMCPTests.ClientJob' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace KillerMCPTests {
public sealed class ClientJob : IDisposable {
    [StructLayout(LayoutKind.Sequential)] struct BasicLimits { public long UserTime; public long JobTime; public uint Flags; public UIntPtr MinSet; public UIntPtr MaxSet; public uint ActiveLimit; public UIntPtr Affinity; public uint Priority; public uint Scheduling; }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong A, B, C, D, E, F; }
    [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct StartupInfo { public int cb; public string Reserved, Desktop, Title; public int X, Y, XSize, YSize, XChars, YChars, Fill, Flags; public short ShowWindow, Reserved2; public IntPtr Reserved3, StdInput, StdOutput, StdError; }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process, Thread; public int ProcessId, ThreadId; }
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, int length);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool CreateProcess(string application, System.Text.StringBuilder commandLine, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    IntPtr job;

    // A kill-on-close job without breakaway, the strictest form an agent client can use.
    public ClientJob() {
        job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        var limits = new ExtendedLimits();
        limits.Basic.Flags = 0x2000;
        if (!SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf(typeof(ExtendedLimits)))) throw new System.ComponentModel.Win32Exception();
    }

    public void Add(IntPtr process) { if (!AssignProcessToJobObject(job, process)) throw new System.ComponentModel.Win32Exception(); }

    // Starts a process already inside the job, as a child of a process in the job would be.
    public int Start(string path, string arguments) {
        var startup = new StartupInfo(); startup.cb = Marshal.SizeOf(typeof(StartupInfo));
        ProcessInfo info;
        var command = new System.Text.StringBuilder("\"" + path + "\" " + arguments);
        if (!CreateProcess(path, command, IntPtr.Zero, IntPtr.Zero, false, 0x4, IntPtr.Zero, null, ref startup, out info)) throw new System.ComponentModel.Win32Exception();
        try {
            Add(info.Process);
            ResumeThread(info.Thread);
            return info.ProcessId;
        } finally { CloseHandle(info.Thread); CloseHandle(info.Process); }
    }

    public bool Closed { get { return job == IntPtr.Zero; } }
    public void Dispose() { if (job != IntPtr.Zero) { CloseHandle(job); job = IntPtr.Zero; } }
}
}
'@
}
