using System.ComponentModel;
using System.Runtime.InteropServices;
using MemSweep.Core.Native;

namespace MemSweep.Core;

/// <summary>以 Win32 唯讀查詢取得 <see cref="SystemSnapshot"/>。</summary>
public sealed unsafe class SystemReader : ISystemReader
{
    private const int DefaultProcessBufferSize = 1 << 20;
    private const int DefaultPageFileBufferSize = 4096;

    // 行程清單的緩衝區在多次取樣間重複使用，不足時加大
    private byte[] _processBuffer;
    private readonly int _pageFileBufferSize;

    public SystemReader()
        : this(DefaultProcessBufferSize, DefaultPageFileBufferSize)
    {
    }

    /// <summary>測試用：指定很小的初始緩衝區，驗證不足時會加大重試。</summary>
    internal SystemReader(int processBufferSize, int pageFileBufferSize)
    {
        _processBuffer = new byte[processBufferSize];
        _pageFileBufferSize = pageFileBufferSize;
    }

    public SystemSnapshot Read()
    {
        var timestamp = DateTimeOffset.Now;
        var performance = ReadPerformance();
        long pageSize = (long)performance.PageSize;
        return new SystemSnapshot(
            timestamp,
            ReadBootTimeUtc(),
            (long)performance.CommitTotal * pageSize,
            (long)performance.CommitLimit * pageSize,
            (long)performance.CommitPeak * pageSize,
            (long)performance.PhysicalTotal * pageSize,
            (long)performance.PhysicalAvailable * pageSize,
            ReadPageFileTotalBytes(pageSize),
            ReadProcesses());
    }

    private static PerformanceInformation ReadPerformance()
    {
        if (!Kernel32.GetPerformanceInfo(out var info, (uint)sizeof(PerformanceInformation)))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        return info;
    }

    private static DateTime ReadBootTimeUtc()
    {
        SystemTimeOfDayInformationData data;
        int status = Ntdll.NtQuerySystemInformation(
            Ntdll.SystemTimeOfDayInformation, &data, (uint)sizeof(SystemTimeOfDayInformationData), out _);
        ThrowIfFailed(status, Ntdll.SystemTimeOfDayInformation);
        return DateTime.FromFileTimeUtc(data.BootTime);
    }

    private long ReadPageFileTotalBytes(long pageSize)
    {
        var buffer = new byte[_pageFileBufferSize];
        while (true)
        {
            int status;
            uint needed;
            fixed (byte* p = buffer)
            {
                status = Ntdll.NtQuerySystemInformation(
                    Ntdll.SystemPageFileInformation, p, (uint)buffer.Length, out needed);
                if (status == 0)
                {
                    return needed == 0 ? 0 : SumPageFilePages(p) * pageSize;
                }
            }
            if (status != Ntdll.StatusInfoLengthMismatch)
            {
                ThrowIfFailed(status, Ntdll.SystemPageFileInformation);
            }
            buffer = new byte[Math.Max(needed, (uint)buffer.Length * 2)];
        }
    }

    private static long SumPageFilePages(byte* buffer)
    {
        long pages = 0;
        uint offset = 0;
        while (true)
        {
            var entry = (SystemPageFileInformationEntry*)(buffer + offset);
            pages += entry->TotalSize;
            if (entry->NextEntryOffset == 0)
            {
                return pages;
            }
            offset += entry->NextEntryOffset;
        }
    }

    private List<ProcessSample> ReadProcesses()
    {
        while (true)
        {
            int status;
            uint needed;
            fixed (byte* p = _processBuffer)
            {
                status = Ntdll.NtQuerySystemInformation(
                    Ntdll.SystemProcessInformation, p, (uint)_processBuffer.Length, out needed);
                if (status == 0)
                {
                    return ParseProcesses(p);
                }
            }
            if (status != Ntdll.StatusInfoLengthMismatch)
            {
                ThrowIfFailed(status, Ntdll.SystemProcessInformation);
            }
            // 兩次呼叫之間可能又多了行程，多留 64 KB
            _processBuffer = new byte[Math.Max(needed + 64 * 1024, (uint)_processBuffer.Length * 2)];
        }
    }

    private static List<ProcessSample> ParseProcesses(byte* buffer)
    {
        var processes = new List<ProcessSample>(capacity: 640);
        uint offset = 0;
        while (true)
        {
            var entry = (SystemProcessInformationEntry*)(buffer + offset);
            int pid = (int)entry->UniqueProcessId;
            string imageName = entry->ImageName.Length == 0
                ? (pid == 0 ? "Idle" : string.Empty)
                : new string((char*)entry->ImageName.Buffer, 0, entry->ImageName.Length / 2);
            processes.Add(new ProcessSample(
                pid,
                (int)entry->InheritedFromUniqueProcessId,
                DateTime.FromFileTimeUtc(entry->CreateTime),
                imageName,
                (long)entry->PagefileUsage,
                (long)entry->WorkingSetSize));
            if (entry->NextEntryOffset == 0)
            {
                return processes;
            }
            offset += entry->NextEntryOffset;
        }
    }

    private static void ThrowIfFailed(int status, int informationClass)
    {
        if (status != 0)
        {
            throw new InvalidOperationException(
                $"NtQuerySystemInformation({informationClass}) 失敗：0x{status:X8}");
        }
    }
}
