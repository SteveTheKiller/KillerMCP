using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KillerMCP.Setup
{
    // Taskkill's tree operation can be denied to a standard user. Capture only this configurator's
    // descendants through Toolhelp, holding process handles so a recycled PID cannot change a target.
    internal sealed class ConfiguratorProcessTree : IDisposable
    {
        private readonly List<Captured> _children = new List<Captured>();
        private sealed class Captured
        {
            internal int Id;
            internal int Depth;
            internal long Created;
            internal NativeHandle Handle = null!;
        }

        internal static ConfiguratorProcessTree Capture(Process root)
        {
            var result = new ConfiguratorProcessTree();
            long expectedRoot;
            try { expectedRoot = root.StartTime.ToUniversalTime().ToFileTimeUtc(); }
            catch (Exception error) when (error is InvalidOperationException || error is System.ComponentModel.Win32Exception) { return result; }
            using var rootHandle = OpenProcess(0x1000, false, root.Id);
            if (rootHandle.IsInvalid || !Creation(rootHandle, out long rootCreated) || rootCreated != expectedRoot) return result;
            long snapshotTime = DateTime.UtcNow.ToFileTimeUtc();
            using var snapshot = CreateToolhelp32Snapshot(2, 0);
            if (snapshot.IsInvalid) return result;
            var entries = new List<ProcessEntry>();
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry)) };
            if (!Process32First(snapshot, ref entry)) return result;
            do { entries.Add(entry); } while (Process32Next(snapshot, ref entry));
            var known = new Dictionary<int, (long Created, int Depth)> { [root.Id] = (rootCreated, 0) };
            bool added;
            do
            {
                added = false;
                foreach (var candidate in entries)
                {
                    int id = unchecked((int)candidate.Id), parent = unchecked((int)candidate.Parent);
                    if (id <= 0 || known.ContainsKey(id) || !known.TryGetValue(parent, out var owner)) continue;
                    var handle = OpenProcess(0x00101001, false, id);
                    if (handle.IsInvalid || !Creation(handle, out long created) || created < owner.Created || created > snapshotTime)
                    {
                        handle.Dispose();
                        continue;
                    }
                    known.Add(id, (created, owner.Depth + 1));
                    result._children.Add(new Captured { Id = id, Depth = owner.Depth + 1, Created = created, Handle = handle });
                    added = true;
                    if (result._children.Count == 256) return result;
                }
            } while (added);
            return result;
        }

        internal void TerminateRemaining()
        {
            _children.Sort((left, right) => right.Depth.CompareTo(left.Depth));
            var deadline = Stopwatch.StartNew();
            foreach (var child in _children)
            {
                if (WaitForSingleObject(child.Handle, 0) != 258 || !Creation(child.Handle, out long created) || created != child.Created) continue;
                if (TerminateProcess(child.Handle, 1))
                {
                    int remaining = Math.Max(0, 5000 - (int)deadline.ElapsedMilliseconds);
                    if (remaining > 0) WaitForSingleObject(child.Handle, (uint)Math.Min(remaining, 2000));
                }
            }
        }

        private static bool Creation(NativeHandle handle, out long created) => GetProcessTimes(handle, out created, out _, out _, out _);
        public void Dispose() { foreach (var child in _children) child.Handle.Dispose(); }

        private sealed class NativeHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public NativeHandle() : base(true) { }
            protected override bool ReleaseHandle() => CloseHandle(handle);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry
        {
            internal uint Size, Usage, Id;
            internal UIntPtr Heap;
            internal uint Module, Threads, Parent;
            internal int Priority;
            internal uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Name;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern NativeHandle CreateToolhelp32Snapshot(uint flags, uint processId);
        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(NativeHandle snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(NativeHandle snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern NativeHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(NativeHandle process, out long created, out long exited, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(NativeHandle process, uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(NativeHandle handle, uint milliseconds);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    }
}
