using System.Runtime.InteropServices;

namespace MemSweep.Core.Native;

/// <summary>kernel32 的唯讀查詢。</summary>
internal static unsafe partial class Kernel32
{
    [LibraryImport("kernel32.dll", EntryPoint = "K32GetPerformanceInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetPerformanceInfo(out PerformanceInformation performanceInformation, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(nint process, uint flags, char* exeName, ref uint size);
}

/// <summary>PERFORMANCE_INFORMATION；數量欄位的單位是分頁。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PerformanceInformation
{
    public uint Size;
    public nuint CommitTotal;
    public nuint CommitLimit;
    public nuint CommitPeak;
    public nuint PhysicalTotal;
    public nuint PhysicalAvailable;
    public nuint SystemCache;
    public nuint KernelTotal;
    public nuint KernelPaged;
    public nuint KernelNonpaged;
    public nuint PageSize;
    public uint HandleCount;
    public uint ProcessCount;
    public uint ThreadCount;
}
