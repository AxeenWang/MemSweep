using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class PeakTrackerTests
{
    private static GroupedSnapshot Sample(double usedGb, double peakGb, DateTimeOffset timestamp)
    {
        var typical = ReportSamples.Typical();
        var snapshot = typical.Snapshot with
        {
            CommitUsedBytes = MemoryMath.FromGigabytes(usedGb),
            CommitPeakBytes = MemoryMath.FromGigabytes(peakGb),
            Timestamp = timestamp,
        };
        return typical with { Snapshot = snapshot };
    }

    [TestMethod]
    public void FirstSampleBelowTheWindowsPeakIsUnknown()
    {
        var tracker = new PeakTracker();

        bool changed = tracker.Update(Sample(31.4, 36.8, ReportSamples.Timestamp));

        Assert.IsTrue(changed);
        var peak = tracker.BootPeak!;
        Assert.AreEqual(MemoryMath.FromGigabytes(36.8), peak.CommitBytes);
        Assert.AreEqual(MemoryMath.FromGigabytes(39.4), peak.CommitLimitBytes);
        Assert.AreEqual(new DateTime(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc), peak.BootTimeUtc);
        Assert.IsFalse(peak.IsKnown);
        Assert.IsNull(peak.Time);
        Assert.IsNull(peak.TopApps);
    }

    [TestMethod]
    public void FirstSampleAtTheWindowsPeakIsKnown()
    {
        var tracker = new PeakTracker();

        tracker.Update(Sample(36.8, 36.8, ReportSamples.Timestamp));

        var peak = tracker.BootPeak!;
        Assert.IsTrue(peak.IsKnown);
        Assert.AreEqual(ReportSamples.Timestamp, peak.Time);
        CollectionAssert.AreEqual(
            new[] { "Claude", "ChatGPT", "Microsoft Edge WebView2", AppGrouper.WindowsGroupName, "Node.js JavaScript Runtime" },
            peak.TopApps!.Select(a => a.Name).ToArray());
        Assert.AreEqual(MemoryMath.FromGigabytes(3.2), peak.TopApps![0].CommitBytes);
    }

    [TestMethod]
    public void RisingWindowsPeakRecordsThisSampleAsTheApproximatePeak()
    {
        var tracker = new PeakTracker();
        tracker.Update(Sample(31.4, 36.8, ReportSamples.Timestamp));
        var later = ReportSamples.Timestamp.AddSeconds(2);

        bool changed = tracker.Update(Sample(37.9, 38.0, later));

        Assert.IsTrue(changed);
        var peak = tracker.BootPeak!;
        Assert.AreEqual(MemoryMath.FromGigabytes(38.0), peak.CommitBytes);
        Assert.AreEqual(later, peak.Time);
        Assert.HasCount(5, peak.TopApps!);
    }

    [TestMethod]
    public void UnchangedWindowsPeakIsNotAnUpdate()
    {
        var tracker = new PeakTracker();
        tracker.Update(Sample(31.4, 36.8, ReportSamples.Timestamp));
        var before = tracker.BootPeak;

        bool changed = tracker.Update(Sample(33.0, 36.8, ReportSamples.Timestamp.AddSeconds(2)));

        Assert.IsFalse(changed);
        Assert.AreSame(before, tracker.BootPeak);
    }

    [TestMethod]
    public void NoPeakBeforeTheFirstSample()
    {
        Assert.IsNull(new PeakTracker().BootPeak);
    }
}
