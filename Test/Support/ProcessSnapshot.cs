using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EricGameLauncher.Testing;

public static class ProcessSnapshot
{
    public sealed class ProcessParent
    {
        public int ProcessId { get; }
        public int ParentProcessId { get; }

        internal ProcessParent(uint processId, uint parentProcessId)
        {
            ProcessId = checked((int)processId);
            ParentProcessId = checked((int)parentProcessId);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Entry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public UIntPtr HeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Executable;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool First(SafeFileHandle snapshot, ref Entry entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Next(SafeFileHandle snapshot, ref Entry entry);

    public static ProcessParent[] Read()
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var entry = new Entry { Size = checked((uint)Marshal.SizeOf<Entry>()) };
        if (!First(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var processes = new List<ProcessParent>();
        do { processes.Add(new ProcessParent(entry.ProcessId, entry.ParentProcessId)); }
        while (Next(snapshot, ref entry));
        int error = Marshal.GetLastWin32Error();
        if (error != 18) throw new Win32Exception(error);
        return processes.ToArray();
    }
}
