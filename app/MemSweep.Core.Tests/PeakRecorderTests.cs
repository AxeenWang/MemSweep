using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class PeakRecorderTests
{
    private const string Warning = "data 資料夾無法寫入，尖峰紀錄已停用";

    private sealed class FakeStore : IPeakStore
    {
        public List<PeakHistory> Saved { get; } = [];

        public int Attempts { get; private set; }

        public Exception? Failure { get; set; }

        public void Save(PeakHistory history)
        {
            Attempts++;
            if (Failure is not null)
            {
                throw Failure;
            }
            Saved.Add(history);
        }
    }

    private static long Gb(double value) => MemoryMath.FromGigabytes(value);

    private static DateTimeOffset At(int day, int hour, int minute, int second = 0) =>
        new(2026, 10, day, hour, minute, second, TimeSpan.FromHours(8));

    private static GroupedSnapshot Sample(double usedGb, DateTimeOffset timestamp)
    {
        var typical = ReportSamples.Typical();
        var snapshot = typical.Snapshot with
        {
            CommitUsedBytes = Gb(usedGb),
            CommitPeakBytes = Gb(Math.Max(usedGb, 36.8)),
            Timestamp = timestamp,
        };
        return typical with { Snapshot = snapshot };
    }

    private static PeakRecorder Recorder(FakeStore store, PeakHistory? saved = null, int retentionDays = 90) =>
        new(new PeakStoreOpening(saved ?? PeakHistory.Empty, store, null), retentionDays);

    private static DailyPeak Day(int month, int day, double usedGb) =>
        new(new DateOnly(2026, month, day), Gb(usedGb), Gb(39.4), new DateTimeOffset(2026, month, day, 12, 0, 0, TimeSpan.FromHours(8)), [new PeakApp("Steam", Gb(1.0))]);

    [TestMethod]
    public void StateBeforeTheFirstSample()
    {
        var saved = new PeakHistory([], [Day(10, 25, 30.0)]);
        var recorder = new PeakRecorder(new PeakStoreOpening(saved, null, Warning), 90);

        Assert.IsNull(recorder.State.BootPeak);
        Assert.AreSame(saved, recorder.State.History);
        Assert.AreEqual(Warning, recorder.State.StorageWarning);
    }

    [TestMethod]
    public void FirstSampleIsWrittenImmediately()
    {
        var store = new FakeStore();
        var recorder = Recorder(store);

        recorder.Record(Sample(31.4, At(26, 14, 0)));

        Assert.HasCount(1, store.Saved);
        Assert.AreSame(recorder.State.History, store.Saved[0]);
        Assert.IsNotNull(recorder.State.BootPeak);
        Assert.HasCount(1, store.Saved[0].Days);
    }

    [TestMethod]
    public void ChangesAreWrittenAtMostEverySixtySeconds()
    {
        var store = new FakeStore();
        var recorder = Recorder(store);
        recorder.Record(Sample(31.4, At(26, 14, 0, 0)));

        recorder.Record(Sample(32.0, At(26, 14, 0, 2)));
        recorder.Record(Sample(32.5, At(26, 14, 0, 58)));

        Assert.HasCount(1, store.Saved);

        recorder.Record(Sample(31.0, At(26, 14, 1, 0)));

        Assert.HasCount(2, store.Saved);
        Assert.AreEqual(Gb(32.5), store.Saved[1].Days[0].CommitBytes);
        Assert.AreEqual(TimeSpan.FromSeconds(60), PeakRecorder.WriteInterval);
    }

    [TestMethod]
    public void NothingIsWrittenWithoutAChange()
    {
        var store = new FakeStore();
        var recorder = Recorder(store);
        recorder.Record(Sample(31.4, At(26, 14, 0)));

        recorder.Record(Sample(30.0, At(26, 14, 5)));

        Assert.AreEqual(1, store.Attempts);
    }

    [TestMethod]
    public void FlushWritesPendingChangesOnce()
    {
        var store = new FakeStore();
        var recorder = Recorder(store);
        recorder.Record(Sample(31.4, At(26, 14, 0, 0)));
        recorder.Record(Sample(33.0, At(26, 14, 0, 2)));

        recorder.Flush();
        recorder.Flush();

        Assert.HasCount(2, store.Saved);
        Assert.AreEqual(Gb(33.0), store.Saved[1].Days[0].CommitBytes);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AFailedWriteIsRetriedAtTheNextInterval(bool accessDenied)
    {
        var store = new FakeStore
        {
            Failure = accessDenied ? new UnauthorizedAccessException() : new IOException("檔案被其他程式使用"),
        };
        var recorder = Recorder(store);
        recorder.Record(Sample(31.4, At(26, 14, 0, 0)));
        store.Failure = null;

        recorder.Record(Sample(30.0, At(26, 14, 0, 2)));
        Assert.AreEqual(1, store.Attempts);

        recorder.Record(Sample(30.0, At(26, 14, 1, 0)));
        Assert.HasCount(1, store.Saved);
        Assert.AreEqual(Gb(31.4), store.Saved[0].Days[0].CommitBytes);
    }

    [TestMethod]
    public void OtherWriteErrorsAreNotHidden()
    {
        var store = new FakeStore { Failure = new NullReferenceException() };

        Assert.ThrowsExactly<NullReferenceException>(() => Recorder(store).Record(Sample(31.4, At(26, 14, 0))));
    }

    [TestMethod]
    public void ClockMovingBackDoesNotDelayWrites()
    {
        var store = new FakeStore();
        var recorder = Recorder(store);
        recorder.Record(Sample(31.4, At(26, 14, 0)));

        recorder.Record(Sample(33.0, At(26, 13, 30)));

        Assert.HasCount(2, store.Saved);
    }

    [TestMethod]
    public void ADisabledStoreStillTracksPeaks()
    {
        var recorder = new PeakRecorder(new PeakStoreOpening(PeakHistory.Empty, null, Warning), 90);

        recorder.Record(Sample(31.4, At(26, 14, 0)));
        recorder.Flush();

        Assert.IsNotNull(recorder.State.BootPeak);
        Assert.HasCount(1, recorder.State.History.Days);
        Assert.AreEqual(Warning, recorder.State.StorageWarning);
    }

    [TestMethod]
    public void RestartWithUnchangedPeaksShowsTheSavedBootPeakWithoutWriting()
    {
        var typical = ReportSamples.Typical().Snapshot;
        var savedBoot = new BootPeak(typical.BootTimeUtc, Gb(36.8), Gb(39.4), At(26, 9, 0), [new PeakApp("Steam", Gb(1.0))]);
        var store = new FakeStore();
        var recorder = Recorder(store, new PeakHistory([savedBoot], [Day(10, 26, 35.0)]));

        recorder.Record(Sample(31.4, At(26, 14, 0)));

        Assert.AreSame(savedBoot, recorder.State.BootPeak);
        Assert.AreEqual(0, store.Attempts);
    }

    [TestMethod]
    public void OldRecordsArePrunedOnTheFirstSampleAndAtMidnight()
    {
        var store = new FakeStore();
        var saved = new PeakHistory([], [Day(10, 24, 30.0), Day(10, 25, 30.0)]);
        var recorder = Recorder(store, saved, retentionDays: 2);

        recorder.Record(Sample(31.4, At(26, 23, 59, 58)));

        CollectionAssert.AreEqual(
            new[] { new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 26) },
            recorder.State.History.Days.Select(d => d.Date).ToArray());

        recorder.Record(Sample(30.0, At(27, 0, 0, 0)));

        CollectionAssert.AreEqual(
            new[] { new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 27) },
            recorder.State.History.Days.Select(d => d.Date).ToArray());
    }

    [TestMethod]
    public void ALowerRetentionIsAppliedAtTheNextSample()
    {
        var store = new FakeStore();
        var saved = new PeakHistory([], [Day(10, 20, 30.0), Day(10, 24, 30.0), Day(10, 25, 30.0)]);
        var recorder = Recorder(store, saved, retentionDays: 90);
        recorder.Record(Sample(31.4, At(26, 14, 0, 0)));
        Assert.HasCount(4, recorder.State.History.Days);

        recorder.SetRetentionDays(2);
        recorder.Record(Sample(30.0, At(26, 14, 0, 2)));

        CollectionAssert.AreEqual(
            new[] { new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 26) },
            recorder.State.History.Days.Select(d => d.Date).ToArray());
    }
}
