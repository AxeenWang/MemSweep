using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class PeakTrackerTests
{
    private static readonly DateTime Boot = new(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Taipei = TimeSpan.FromHours(8);

    private static long Gb(double value) => MemoryMath.FromGigabytes(value);

    private static DateTimeOffset At(int month, int day, int hour, int minute, int second = 0) =>
        new(2026, month, day, hour, minute, second, Taipei);

    private static GroupedSnapshot Sample(double usedGb, double peakGb, DateTimeOffset timestamp, DateTime? bootUtc = null)
    {
        var typical = ReportSamples.Typical();
        var snapshot = typical.Snapshot with
        {
            CommitUsedBytes = Gb(usedGb),
            CommitPeakBytes = Gb(peakGb),
            Timestamp = timestamp,
            BootTimeUtc = bootUtc ?? Boot,
        };
        return typical with { Snapshot = snapshot };
    }

    private static BootPeak SavedBoot(DateTime bootUtc, double peakGb, DateTimeOffset? time) =>
        new(bootUtc, Gb(peakGb), Gb(39.4), time, time is null ? null : [new PeakApp("Steam", Gb(1.0))]);

    private static DailyPeak SavedDay(DateOnly date, double usedGb, DateTimeOffset time) =>
        new(date, Gb(usedGb), Gb(39.4), time, [new PeakApp("Steam", Gb(1.0))]);

    [TestMethod]
    public void NoPeaksBeforeTheFirstSample()
    {
        var tracker = new PeakTracker();

        Assert.IsNull(tracker.BootPeak);
        Assert.IsNull(tracker.TodayPeak);
        Assert.IsEmpty(tracker.History.Boots);
        Assert.IsEmpty(tracker.History.Days);
    }

    [TestMethod]
    public void FirstSampleBelowTheWindowsPeakIsUnknown()
    {
        var tracker = new PeakTracker();

        bool changed = tracker.Update(Sample(31.4, 36.8, ReportSamples.Timestamp));

        Assert.IsTrue(changed);
        var peak = tracker.BootPeak!;
        Assert.AreEqual(Gb(36.8), peak.CommitBytes);
        Assert.AreEqual(Gb(39.4), peak.CommitLimitBytes);
        Assert.AreEqual(Boot, peak.BootTimeUtc);
        Assert.IsFalse(peak.IsKnown);
        Assert.IsNull(peak.Time);
        Assert.IsNull(peak.TopApps);
        CollectionAssert.AreEqual(new[] { peak }, tracker.History.Boots.ToArray());
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
        Assert.AreEqual(Gb(3.2), peak.TopApps![0].CommitBytes);
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
        Assert.AreEqual(Gb(38.0), peak.CommitBytes);
        Assert.AreEqual(later, peak.Time);
        Assert.HasCount(5, peak.TopApps!);
        Assert.HasCount(1, tracker.History.Boots);
    }

    [TestMethod]
    public void UnchangedPeaksAreNotAnUpdate()
    {
        var tracker = new PeakTracker();
        tracker.Update(Sample(31.4, 36.8, ReportSamples.Timestamp));
        var before = tracker.History;

        bool changed = tracker.Update(Sample(30.0, 36.8, ReportSamples.Timestamp.AddSeconds(2)));

        Assert.IsFalse(changed);
        Assert.AreSame(before, tracker.History);
    }

    [TestMethod]
    public void FirstSampleStartsTodaysPeakFromTheSampleOnly()
    {
        var tracker = new PeakTracker();

        tracker.Update(Sample(31.4, 36.8, ReportSamples.Timestamp));

        // 每日尖峰只看取樣到的 commit 已用，不看 Windows 的開機以來尖峰（F-32）
        var today = tracker.TodayPeak!;
        Assert.AreEqual(new DateOnly(2026, 10, 26), today.Date);
        Assert.AreEqual(Gb(31.4), today.CommitBytes);
        Assert.AreEqual(Gb(39.4), today.CommitLimitBytes);
        Assert.AreEqual(ReportSamples.Timestamp, today.Time);
        Assert.HasCount(5, today.TopApps);
        Assert.AreEqual("Claude", today.TopApps[0].Name);
    }

    [TestMethod]
    public void HigherSampleRaisesTodaysPeakAndLowerSampleKeepsIt()
    {
        var tracker = new PeakTracker();
        tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0)));

        Assert.IsTrue(tracker.Update(Sample(33.5, 36.8, At(10, 26, 14, 1))));
        Assert.IsFalse(tracker.Update(Sample(32.0, 36.8, At(10, 26, 14, 2))));
        // 和目前紀錄相同不算新高，時間維持第一次達到的時刻
        Assert.IsFalse(tracker.Update(Sample(33.5, 36.8, At(10, 26, 14, 3))));

        var today = tracker.TodayPeak!;
        Assert.AreEqual(Gb(33.5), today.CommitBytes);
        Assert.AreEqual(At(10, 26, 14, 1), today.Time);
        Assert.HasCount(1, tracker.History.Days);
    }

    [TestMethod]
    public void MidnightStartsANewDailyPeak()
    {
        var tracker = new PeakTracker();
        tracker.Update(Sample(33.0, 36.8, At(10, 26, 23, 59, 58)));

        bool changed = tracker.Update(Sample(30.0, 36.8, At(10, 27, 0, 0, 0)));

        Assert.IsTrue(changed);
        var days = tracker.History.Days;
        Assert.HasCount(2, days);
        Assert.AreEqual(new DateOnly(2026, 10, 26), days[0].Date);
        Assert.AreEqual(Gb(33.0), days[0].CommitBytes);
        Assert.AreEqual(new DateOnly(2026, 10, 27), days[1].Date);
        Assert.AreEqual(Gb(30.0), days[1].CommitBytes);
        Assert.AreSame(days[1], tracker.TodayPeak);
    }

    [TestMethod]
    public void HistoryIsReplacedInsteadOfModified()
    {
        var tracker = new PeakTracker();
        tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0)));
        var before = tracker.History;

        tracker.Update(Sample(33.5, 36.8, At(10, 26, 14, 1)));

        Assert.AreNotSame(before, tracker.History);
        Assert.AreEqual(Gb(31.4), before.Days[0].CommitBytes);
        Assert.AreEqual(Gb(33.5), tracker.History.Days[0].CommitBytes);
    }

    [TestMethod]
    public void RestartOnTheSameDayContinuesTodaysPeak()
    {
        var saved = SavedDay(new DateOnly(2026, 10, 26), 35.0, At(10, 26, 9, 0));
        var tracker = new PeakTracker(new PeakHistory([], [saved]));

        tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0)));

        Assert.AreSame(saved, tracker.TodayPeak);
        Assert.HasCount(1, tracker.History.Days);

        tracker.Update(Sample(35.5, 36.8, At(10, 26, 14, 1)));

        Assert.AreEqual(Gb(35.5), tracker.TodayPeak!.CommitBytes);
        Assert.AreEqual(At(10, 26, 14, 1), tracker.TodayPeak.Time);
        Assert.HasCount(1, tracker.History.Days);
    }

    [TestMethod]
    public void RestartWithinTheSameBootKeepsTheSavedPeak()
    {
        // CSV 只記到秒與 0.01 GB：開機時間差 1 秒、Windows 的尖峰比紀錄多 3 MB，仍是同一個尖峰
        var saved = SavedBoot(Boot.AddSeconds(-1), 36.80, At(10, 26, 12, 10));
        var tracker = new PeakTracker(new PeakHistory([saved], []));
        long windowsPeak = Gb(36.80) + 3 * 1024 * 1024;

        var sample = Sample(31.4, 36.8, At(10, 26, 14, 0));
        tracker.Update(sample with { Snapshot = sample.Snapshot with { CommitPeakBytes = windowsPeak } });

        var peak = tracker.BootPeak!;
        Assert.AreEqual(At(10, 26, 12, 10), peak.Time);
        Assert.AreSame(saved.TopApps, peak.TopApps);
        Assert.AreEqual(saved.BootTimeUtc, peak.BootTimeUtc);
        Assert.AreEqual(windowsPeak, peak.CommitBytes);
        Assert.HasCount(1, tracker.History.Boots);
    }

    [TestMethod]
    public void RestartWithTheSameValueIsNotABootChange()
    {
        var saved = SavedBoot(Boot, 36.8, At(10, 26, 12, 10));
        var savedDay = SavedDay(new DateOnly(2026, 10, 26), 35.0, At(10, 26, 9, 0));
        var tracker = new PeakTracker(new PeakHistory([saved], [savedDay]));

        bool changed = tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0)));

        Assert.IsFalse(changed);
        Assert.AreSame(saved, tracker.BootPeak);
    }

    [TestMethod]
    public void HigherPeakWhileNotRunningReplacesTheSavedPeakWithAnUnknownOne()
    {
        var saved = SavedBoot(Boot, 36.8, At(10, 26, 12, 10));
        var tracker = new PeakTracker(new PeakHistory([saved], []));

        tracker.Update(Sample(31.4, 38.0, At(10, 26, 14, 0)));

        var peak = tracker.BootPeak!;
        Assert.IsFalse(peak.IsKnown);
        Assert.AreEqual(Gb(38.0), peak.CommitBytes);
        Assert.HasCount(1, tracker.History.Boots);
    }

    [TestMethod]
    public void ADifferentBootStartsANewRecord()
    {
        var earlier = SavedBoot(new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc), 39.0, At(10, 25, 20, 0));
        var tracker = new PeakTracker(new PeakHistory([earlier], []));

        tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0)));

        var boots = tracker.History.Boots;
        Assert.HasCount(2, boots);
        Assert.AreSame(earlier, boots[0]);
        Assert.AreEqual(Boot, boots[1].BootTimeUtc);
        Assert.AreEqual(Gb(36.8), boots[1].CommitBytes);
    }

    [TestMethod]
    public void BootTimeOffByMoreThanTheToleranceIsADifferentBoot()
    {
        var saved = SavedBoot(Boot.AddSeconds(-61), 36.8, At(10, 26, 12, 10));
        var tracker = new PeakTracker(new PeakHistory([saved], []));

        tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0)));

        Assert.HasCount(2, tracker.History.Boots);
        Assert.IsFalse(tracker.BootPeak!.IsKnown);
        Assert.AreEqual(TimeSpan.FromSeconds(60), PeakTracker.BootTimeTolerance);
    }

    [TestMethod]
    public void PruneKeepsTheRetentionDaysIncludingToday()
    {
        // 保留 90 天：2026-10-26 往前算，2026-07-29 是保留的第一天
        var history = new PeakHistory(
            [
                SavedBoot(new DateTime(2026, 7, 27, 1, 0, 0, DateTimeKind.Utc), 30.0, At(7, 28, 23, 0)),
                // 時間未知時以開機時間的本機日期判斷：UTC 07-28 18:00 是本機 07-29 02:00
                SavedBoot(new DateTime(2026, 7, 28, 18, 0, 0, DateTimeKind.Utc), 30.0, null),
            ],
            [
                SavedDay(new DateOnly(2026, 7, 28), 30.0, At(7, 28, 12, 0)),
                SavedDay(new DateOnly(2026, 7, 29), 30.0, At(7, 29, 12, 0)),
            ]);
        var tracker = new PeakTracker(history);
        tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0)));

        bool removed = tracker.Prune(At(10, 26, 14, 0), 90);

        Assert.IsTrue(removed);
        CollectionAssert.AreEqual(
            new[] { new DateTime(2026, 7, 28, 18, 0, 0, DateTimeKind.Utc), Boot },
            tracker.History.Boots.Select(b => b.BootTimeUtc).ToArray());
        CollectionAssert.AreEqual(
            new[] { new DateOnly(2026, 7, 29), new DateOnly(2026, 10, 26) },
            tracker.History.Days.Select(d => d.Date).ToArray());
    }

    [TestMethod]
    public void PruneNeverRemovesThisBootOrToday()
    {
        // 快速啟動讓一次開機可以跨很多天；本次開機的紀錄即使早於保留天數也要保留
        var tracker = new PeakTracker();
        tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));

        bool removed = tracker.Prune(At(10, 26, 14, 0), 1);

        Assert.IsFalse(removed);
        Assert.HasCount(1, tracker.History.Boots);
        Assert.HasCount(1, tracker.History.Days);
    }

    [TestMethod]
    public void PruneWithNothingToRemoveKeepsTheHistory()
    {
        var tracker = new PeakTracker();
        tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0)));
        var before = tracker.History;

        Assert.IsFalse(tracker.Prune(At(10, 26, 14, 0), 90));
        Assert.AreSame(before, tracker.History);
    }
}
