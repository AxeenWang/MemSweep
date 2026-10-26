using System.Runtime.InteropServices;

namespace MemSweep.Core.Native;

/// <summary>ntdll 的唯讀查詢。</summary>
internal static unsafe partial class Ntdll
{
    public const int SystemTimeOfDayInformation = 3;
    public const int SystemProcessInformation = 5;
    public const int SystemPageFileInformation = 18;
    public const int ProcessCommandLineInformation = 60;
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(
        int systemInformationClass,
        void* systemInformation,
        uint systemInformationLength,
        out uint returnLength);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryInformationProcess(
        nint processHandle,
        int processInformationClass,
        void* processInformation,
        uint processInformationLength,
        out uint returnLength);
}

[StructLayout(LayoutKind.Sequential)]
internal struct UnicodeString
{
    public ushort Length;
    public ushort MaximumLength;
    public nint Buffer;
}

/// <summary>SYSTEM_TIMEOFDAY_INFORMATION；時間為 FILETIME（UTC）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SystemTimeOfDayInformationData
{
    public long BootTime;
    public long CurrentTime;
    public long TimeZoneBias;
    public uint TimeZoneId;
    public uint Reserved;
    public ulong BootTimeBias;
    public ulong SleepTimeBias;
}

/// <summary>SYSTEM_PROCESS_INFORMATION 的固定部分（之後接著執行緒陣列，這裡用不到）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SystemProcessInformationEntry
{
    public uint NextEntryOffset;
    public uint NumberOfThreads;
    public long WorkingSetPrivateSize;
    public uint HardFaultCount;
    public uint NumberOfThreadsHighWatermark;
    public ulong CycleTime;
    public long CreateTime;
    public long UserTime;
    public long KernelTime;
    public UnicodeString ImageName;
    public int BasePriority;
    public nint UniqueProcessId;
    public nint InheritedFromUniqueProcessId;
    public uint HandleCount;
    public uint SessionId;
    public nuint UniqueProcessKey;
    public nuint PeakVirtualSize;
    public nuint VirtualSize;
    public uint PageFaultCount;
    public nuint PeakWorkingSetSize;
    public nuint WorkingSetSize;
    public nuint QuotaPeakPagedPoolUsage;
    public nuint QuotaPagedPoolUsage;
    public nuint QuotaPeakNonPagedPoolUsage;
    public nuint QuotaNonPagedPoolUsage;
    public nuint PagefileUsage;
    public nuint PeakPagefileUsage;
    public nuint PrivatePageCount;
}

/// <summary>SYSTEM_PAGEFILE_INFORMATION；大小欄位的單位是分頁。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SystemPageFileInformationEntry
{
    public uint NextEntryOffset;
    public uint TotalSize;
    public uint TotalInUse;
    public uint PeakUsage;
    public UnicodeString PageFileName;
}
