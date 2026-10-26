using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
[TestCategory("Live")]
public sealed class MemorySamplerLiveTests
{
    [TestMethod]
    public void EveryProcessBelongsToExactlyOneApp()
    {
        var grouped = MemorySampler.CreateDefault().Sample();
        var processes = grouped.Snapshot.Processes;
        long processCommit = processes.Sum(p => p.PrivateCommitBytes);

        Assert.AreEqual(processes.Count, grouped.Apps.Sum(a => a.ProcessCount));
        Assert.AreEqual(processCommit, grouped.Apps.Sum(a => a.CommitBytes));
        Assert.AreEqual(Math.Max(0, grouped.Snapshot.CommitUsedBytes - processCommit), grouped.UnattributedBytes);
    }

    [TestMethod]
    public void WindowsRowHoldsTheSystemProcesses()
    {
        var grouped = MemorySampler.CreateDefault().Sample();

        var windows = grouped.Apps.Single(a => a.IsWindows);
        Assert.AreEqual(AppGrouper.WindowsGroupName, windows.Name);
        Assert.IsTrue(windows.Components.Any(c => c.Name == "System"));
        Assert.IsTrue(windows.Components.Any(c => c.Name == "svchost.exe"));
    }
}
