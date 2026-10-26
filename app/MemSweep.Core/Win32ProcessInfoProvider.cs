using System.Diagnostics;
using MemSweep.Core.Native;

namespace MemSweep.Core;

/// <summary>以 PROCESS_QUERY_LIMITED_INFORMATION 唯讀查詢行程資訊；開不了的行程回傳 <see cref="ProcessInfo.Unknown"/>。</summary>
public sealed unsafe class Win32ProcessInfoProvider : IProcessInfoProvider
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    // 版本資訊以執行檔路徑快取，同一支程式的多個行程只讀一次
    private readonly Dictionary<string, (string? Company, string? Description)> _versionInfo =
        new(StringComparer.OrdinalIgnoreCase);

    public ProcessInfo GetInfo(ProcessSample process)
    {
        // Idle 與 System 沒有執行檔
        if (process.Pid is 0 or 4)
        {
            return ProcessInfo.Unknown;
        }

        nint handle = Kernel32.OpenProcess(ProcessQueryLimitedInformation, false, (uint)process.Pid);
        if (handle == 0)
        {
            return ProcessInfo.Unknown;
        }

        try
        {
            string? path = QueryImagePath(handle);
            string? label = string.Equals(process.ImageName, "node.exe", StringComparison.OrdinalIgnoreCase)
                ? CommandLineLabel.FromCommandLine(QueryCommandLine(handle) ?? string.Empty)
                : null;
            var (company, description) = path is null ? (null, null) : GetVersionInfo(path);
            return new ProcessInfo(path, company, description, label);
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }
    }

    /// <summary>測試用：讀取指定行程的命令列；開不了時回傳 null。</summary>
    internal static string? ReadCommandLine(int pid)
    {
        nint handle = Kernel32.OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (handle == 0)
        {
            return null;
        }

        try
        {
            return QueryCommandLine(handle);
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }
    }

    private static string? QueryImagePath(nint handle)
    {
        const int capacity = 1024;
        char* buffer = stackalloc char[capacity];
        uint size = capacity;
        return Kernel32.QueryFullProcessImageName(handle, 0, buffer, ref size)
            ? new string(buffer, 0, (int)size)
            : null;
    }

    private static string? QueryCommandLine(nint handle)
    {
        var buffer = new byte[4096];
        while (true)
        {
            int status;
            uint needed;
            fixed (byte* p = buffer)
            {
                status = Ntdll.NtQueryInformationProcess(
                    handle, Ntdll.ProcessCommandLineInformation, p, (uint)buffer.Length, out needed);
                if (status == 0)
                {
                    var text = (UnicodeString*)p;
                    return text->Length == 0 ? string.Empty : new string((char*)text->Buffer, 0, text->Length / 2);
                }
            }
            if (status != Ntdll.StatusInfoLengthMismatch || needed <= buffer.Length)
            {
                return null;
            }
            buffer = new byte[needed];
        }
    }

    private (string? Company, string? Description) GetVersionInfo(string path)
    {
        if (!_versionInfo.TryGetValue(path, out var info))
        {
            try
            {
                var version = FileVersionInfo.GetVersionInfo(path);
                info = (Normalize(version.CompanyName), Normalize(version.FileDescription));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                info = (null, null);
            }
            _versionInfo[path] = info;
        }
        return info;
    }

    private static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
