using System.Diagnostics;
using System.Runtime.InteropServices;
using Whitebox.Core;

namespace Whitebox.Services;

public readonly record struct ProcSample(int Pid, string Name, long CpuTime, long PrivateBytes, long WorkingSet, int Threads, int Handles);

public static class ProcessService
{
    private const int SystemProcessInformation = 5;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    /// <summary>
    /// Snapshot of every process in a single NtQuerySystemInformation call — far cheaper than
    /// System.Diagnostics.Process, and it never throws for protected processes. Offsets are x64.
    /// </summary>
    public static List<ProcSample> Snapshot()
    {
        int size = 1 << 20;
        while (true)
        {
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                int status = Native.NtQuerySystemInformation(SystemProcessInformation, buffer, size, out int needed);
                if (status == StatusInfoLengthMismatch)
                {
                    size = Math.Max(size * 2, needed + 65536);
                    continue;
                }
                if (status != 0) return [];

                var list = new List<ProcSample>(512);
                int offset = 0;
                while (true)
                {
                    IntPtr p = buffer + offset;
                    int next = Marshal.ReadInt32(p, 0);
                    int threads = Marshal.ReadInt32(p, 4);
                    long privateWs = Marshal.ReadInt64(p, 8);
                    long userTime = Marshal.ReadInt64(p, 40);
                    long kernelTime = Marshal.ReadInt64(p, 48);
                    int nameLength = (ushort)Marshal.ReadInt16(p, 56);
                    IntPtr nameBuffer = Marshal.ReadIntPtr(p, 64);
                    int pid = (int)Marshal.ReadInt64(p, 80);
                    int handles = Marshal.ReadInt32(p, 96);
                    long workingSet = Marshal.ReadInt64(p, 144);

                    if (pid != 0)
                    {
                        string name = nameLength > 0 && nameBuffer != IntPtr.Zero
                            ? Marshal.PtrToStringUni(nameBuffer, nameLength / 2)
                            : "System";
                        list.Add(new ProcSample(pid, name, userTime + kernelTime, privateWs, workingSet, threads, handles));
                    }

                    if (next == 0) break;
                    offset += next;
                }
                return list;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    public static bool Kill(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsRunning(params string[] names)
    {
        var set = new HashSet<string>(names.Select(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n : n + ".exe"),
            StringComparer.OrdinalIgnoreCase);
        return Snapshot().Any(s => set.Contains(s.Name));
    }

    /// <summary>
    /// Trims every accessible process' working set; with admin rights also purges the standby list.
    /// Returns (bytes trimmed from working sets, bytes of available memory gained).
    /// </summary>
    public static (long Trimmed, long Gained) OptimizeMemory()
    {
        var before = Snapshot();
        long wsBefore = before.Sum(s => s.WorkingSet);
        long availBefore = (long)Native.Memory().ullAvailPhys;
        int self = Environment.ProcessId;

        foreach (var s in before)
        {
            if (s.Pid == self || s.Pid <= 4) continue;
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.PROCESS_SET_QUOTA, false, s.Pid);
            if (h == IntPtr.Zero) continue;
            Native.EmptyWorkingSet(h);
            Native.CloseHandle(h);
        }

        if (AdminHelper.IsAdmin && Native.EnablePrivilege("SeProfileSingleProcessPrivilege"))
        {
            const int SystemMemoryListInformation = 80;
            int command = 4; // MemoryPurgeStandbyList
            Native.NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
        }

        Thread.Sleep(600);
        long wsAfter = Snapshot().Sum(s => s.WorkingSet);
        long availAfter = (long)Native.Memory().ullAvailPhys;
        return (Math.Max(0, wsBefore - wsAfter), Math.Max(0, availAfter - availBefore));
    }
}
