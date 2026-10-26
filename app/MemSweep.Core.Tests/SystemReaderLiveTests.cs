using System.Diagnostics;
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
[TestCategory("Live")]
public sealed class SystemReaderLiveTests
{
    private const double Megabytes64 = 64.0 * 1024 * 1024;

    [TestMethod]
    public void ReadsCommitPhysicalMemoryAndBootTime()
    {
        var snapshot = new SystemReader().Read();

        Assert.IsGreaterThan(0L, snapshot.CommitUsedBytes);
        Assert.IsGreaterThan(snapshot.CommitUsedBytes, snapshot.CommitLimitBytes);
        Assert.IsGreaterThanOrEqualTo(snapshot.CommitUsedBytes, snapshot.CommitPeakBytes);
        Assert.IsGreaterThan(snapshot.PhysicalAvailableBytes, snapshot.PhysicalTotalBytes);
        Assert.IsLessThan(DateTime.UtcNow, snapshot.BootTimeUtc);
        Assert.IsGreaterThan(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), snapshot.BootTimeUtc);
    }

    [TestMethod]
    public void CommitMatchesGlobalMemoryStatusEx()
    {
        var snapshot = new SystemReader().Read();
        var status = TestNative.GetMemoryStatus();

        // ullTotalPageFile 就是 commit 上限；已用量在兩次查詢之間會變動，只要求相差 1 GB 以內
        Assert.AreEqual((double)status.TotalPageFile, snapshot.CommitLimitBytes, Megabytes64);
        Assert.AreEqual((double)status.TotalPhys, snapshot.PhysicalTotalBytes, Megabytes64);
        long usedByStatus = (long)(status.TotalPageFile - status.AvailPageFile);
        Assert.AreEqual((double)usedByStatus, snapshot.CommitUsedBytes, MemoryMath.BytesPerGigabyte);
    }

    [TestMethod]
    public void CommitLimitIsPhysicalMemoryPlusPageFiles()
    {
        var snapshot = new SystemReader().Read();

        Assert.IsGreaterThanOrEqualTo(0L, snapshot.PageFileTotalBytes);
        Assert.AreEqual(
            (double)(snapshot.PhysicalTotalBytes + snapshot.PageFileTotalBytes),
            snapshot.CommitLimitBytes,
            MemoryMath.BytesPerGigabyte);
    }

    [TestMethod]
    public void ListsCurrentProcessWithItsCommit()
    {
        using var current = Process.GetCurrentProcess();
        var snapshot = new SystemReader().Read();
        current.Refresh();

        var self = snapshot.Processes.Single(p => p.Pid == Environment.ProcessId);
        Assert.AreEqual(current.ProcessName + ".exe", self.ImageName, ignoreCase: true);
        Assert.IsGreaterThan(0, self.ParentPid);
        Assert.AreEqual((double)current.PrivateMemorySize64, self.PrivateCommitBytes, current.PrivateMemorySize64 * 0.25);
        Assert.IsGreaterThan(0L, self.WorkingSetBytes);
        Assert.IsLessThan(1.0, Math.Abs((current.StartTime.ToUniversalTime() - self.CreateTimeUtc).TotalSeconds));
    }

    [TestMethod]
    public void GrowsBuffersThatAreTooSmall()
    {
        var expected = new SystemReader().Read();
        var snapshot = new SystemReader(processBufferSize: 16, pageFileBufferSize: 16).Read();

        Assert.AreEqual(expected.PageFileTotalBytes, snapshot.PageFileTotalBytes);
        Assert.IsGreaterThan(50, snapshot.Processes.Count);
        Assert.IsTrue(snapshot.Processes.Any(p => p.Pid == Environment.ProcessId));
    }

    [TestMethod]
    public void ListsIdleAndSystemProcesses()
    {
        var snapshot = new SystemReader().Read();

        Assert.AreEqual("Idle", snapshot.Processes.Single(p => p.Pid == 0).ImageName);
        Assert.AreEqual("System", snapshot.Processes.Single(p => p.Pid == 4).ImageName);
        Assert.IsGreaterThan(50, snapshot.Processes.Count);
    }
}
