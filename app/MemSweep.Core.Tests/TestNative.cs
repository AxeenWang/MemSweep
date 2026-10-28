using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MemSweep.Core.Tests;

/// <summary>即時系統測試用來交叉比對的另一組 API。</summary>
internal static class TestNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    public static MemoryStatusEx GetMemoryStatus()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        return status;
    }

    // PROCESS_MEMORY_COUNTERS_EX2：PrivateWorkingSetSize 是另一個取得私用工作集的 API
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx2
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
        public nuint PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(nint process, ref ProcessMemoryCountersEx2 counters, uint size);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    /// <summary>目前行程的私用工作集。</summary>
    public static long GetPrivateWorkingSet()
    {
        var counters = new ProcessMemoryCountersEx2 { Size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx2>() };
        if (!GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Size))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        return (long)counters.PrivateWorkingSetSize;
    }
}
