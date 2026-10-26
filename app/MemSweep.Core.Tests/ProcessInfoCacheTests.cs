using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class ProcessInfoCacheTests
{
    private static readonly DateTime Created = new(2026, 10, 26, 1, 0, 0, DateTimeKind.Utc);

    private sealed class CountingProvider : IProcessInfoProvider
    {
        public List<ProcessSample> Calls { get; } = new();

        public ProcessInfo GetInfo(ProcessSample process)
        {
            Calls.Add(process);
            return new ProcessInfo($@"C:\apps\{process.ImageName}#{Calls.Count}", null, null, null);
        }
    }

    private static ProcessSample Sample(int pid, string name, DateTime created) =>
        new(pid, 1, created, name, 0, 0);

    [TestMethod]
    public void QueriesEachProcessOnce()
    {
        var provider = new CountingProvider();
        var cache = new ProcessInfoCache(provider);
        var processes = new[] { Sample(100, "a.exe", Created), Sample(200, "b.exe", Created) };

        var first = cache.Resolve(processes);
        var second = cache.Resolve(processes);

        Assert.HasCount(2, provider.Calls);
        Assert.AreEqual(@"C:\apps\a.exe#1", first[100].ExecutablePath);
        Assert.AreEqual(@"C:\apps\b.exe#2", second[200].ExecutablePath);
    }

    [TestMethod]
    public void RequeriesWhenPidIsReused()
    {
        var provider = new CountingProvider();
        var cache = new ProcessInfoCache(provider);
        cache.Resolve([Sample(100, "a.exe", Created)]);

        var result = cache.Resolve([Sample(100, "c.exe", Created.AddMinutes(5))]);

        Assert.HasCount(2, provider.Calls);
        Assert.AreEqual(@"C:\apps\c.exe#2", result[100].ExecutablePath);
    }

    [TestMethod]
    public void ForgetsExitedProcesses()
    {
        var provider = new CountingProvider();
        var cache = new ProcessInfoCache(provider);
        var a = Sample(100, "a.exe", Created);
        cache.Resolve([a]);
        cache.Resolve([Sample(200, "b.exe", Created)]);

        var result = cache.Resolve([a]);

        Assert.HasCount(3, provider.Calls);
        Assert.AreEqual(@"C:\apps\a.exe#3", result[100].ExecutablePath);
    }
}
