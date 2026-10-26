namespace MemSweep.Core;

/// <summary>取樣並歸組：SystemReader → ProcessInfoCache → AppGrouper。</summary>
public sealed class MemorySampler
{
    private readonly ISystemReader _reader;
    private readonly ProcessInfoCache _cache;
    private readonly AppGrouper _grouper;

    public MemorySampler(ISystemReader reader, IProcessInfoProvider provider, AppGrouper grouper)
    {
        _reader = reader;
        _cache = new ProcessInfoCache(provider);
        _grouper = grouper;
    }

    public static MemorySampler CreateDefault() =>
        new(new SystemReader(), new Win32ProcessInfoProvider(), new AppGrouper());

    public GroupedSnapshot Sample()
    {
        var snapshot = _reader.Read();
        return _grouper.Group(snapshot, _cache.Resolve(snapshot.Processes));
    }
}
