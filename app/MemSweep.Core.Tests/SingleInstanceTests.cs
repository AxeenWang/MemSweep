using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class SingleInstanceTests
{
    // 每個測試用不同的名稱，不會碰到正在執行的 MemSweep
    private static (string Mutex, string Event) UniqueNames()
    {
        string id = Guid.NewGuid().ToString("N");
        return ($@"Local\MemSweep.Test.{id}", $@"Local\MemSweep.Test.{id}.ShowPanel");
    }

    [TestMethod]
    public void SecondInstanceAsksTheFirstToShowThePanel()
    {
        var (mutex, ev) = UniqueNames();
        using var first = SingleInstance.TryAcquire(mutex, ev);
        Assert.IsNotNull(first);
        using var shown = new ManualResetEventSlim();
        first.ShowRequested += shown.Set;

        var second = SingleInstance.TryAcquire(mutex, ev);

        Assert.IsNull(second);
        Assert.IsTrue(shown.Wait(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public void NameIsFreeAgainAfterDispose()
    {
        var (mutex, ev) = UniqueNames();
        SingleInstance.TryAcquire(mutex, ev)!.Dispose();

        using var again = SingleInstance.TryAcquire(mutex, ev);

        Assert.IsNotNull(again);
    }
}
