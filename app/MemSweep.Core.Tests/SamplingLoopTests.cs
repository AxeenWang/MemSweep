using System.ComponentModel;
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class SamplingLoopTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(20);

    [TestMethod]
    public async Task PublishesEverySample()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var published = new List<GroupedSnapshot>();
        var sample = ReportSamples.Typical();
        var loop = new SamplingLoop(
            () => sample,
            g =>
            {
                published.Add(g);
                if (published.Count == 3)
                {
                    cts.Cancel();
                }
            },
            Fast);

        await loop.RunAsync(cts.Token);

        Assert.HasCount(3, published);
        Assert.AreSame(sample, published[0]);
    }

    [TestMethod]
    public async Task SkipsFailedSamplesAndKeepsRunning()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int calls = 0;
        int published = 0;
        var loop = new SamplingLoop(
            () =>
            {
                calls++;
                return calls switch
                {
                    1 => throw new Win32Exception(5),
                    2 => throw new InvalidOperationException("NtQuerySystemInformation(5) 失敗：0xC0000017"),
                    _ => ReportSamples.Typical(),
                };
            },
            _ =>
            {
                published++;
                if (published == 2)
                {
                    cts.Cancel();
                }
            },
            Fast);

        await loop.RunAsync(cts.Token);

        Assert.AreEqual(4, calls);
        Assert.AreEqual(2, published);
    }

    [TestMethod]
    public async Task OtherExceptionsStopTheLoop()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var loop = new SamplingLoop(() => throw new NullReferenceException(), _ => { }, Fast);

        await Assert.ThrowsExactlyAsync<NullReferenceException>(() => loop.RunAsync(cts.Token));
    }

    [TestMethod]
    public void DefaultPeriodIsTwoSeconds()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(2), SamplingLoop.DefaultPeriod);
    }
}
