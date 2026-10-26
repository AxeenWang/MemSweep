namespace MemSweep.Core;

/// <summary>
/// 以（PID、建立時間）為鍵快取 <see cref="ProcessInfo"/>：同一個行程只向 provider 查一次，
/// PID 被重複使用時會重新查詢，已結束的行程從快取移除。
/// </summary>
public sealed class ProcessInfoCache
{
    private readonly IProcessInfoProvider _provider;
    private Dictionary<(int Pid, DateTime CreateTimeUtc), ProcessInfo> _entries = new();

    public ProcessInfoCache(IProcessInfoProvider provider)
    {
        _provider = provider;
    }

    /// <summary>回傳這次快照中每個 PID 的行程資訊。</summary>
    public IReadOnlyDictionary<int, ProcessInfo> Resolve(IReadOnlyList<ProcessSample> processes)
    {
        var next = new Dictionary<(int Pid, DateTime CreateTimeUtc), ProcessInfo>(processes.Count);
        var result = new Dictionary<int, ProcessInfo>(processes.Count);
        foreach (var process in processes)
        {
            var key = (process.Pid, process.CreateTimeUtc);
            if (!_entries.TryGetValue(key, out var info))
            {
                info = _provider.GetInfo(process);
            }
            next[key] = info;
            result[process.Pid] = info;
        }
        _entries = next;
        return result;
    }
}
