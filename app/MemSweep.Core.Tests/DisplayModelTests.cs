using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class DisplayModelTests
{
    private static readonly DateTime Boot = new(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc);

    private static BootPeak KnownPeak(DateTimeOffset time) =>
        new(Boot, MemoryMath.FromGigabytes(36.8), MemoryMath.FromGigabytes(39.4), time, [new PeakApp("Claude", 1)]);

    [TestMethod]
    [DataRow(0, UsageLevel.Green)]
    [DataRow(69, UsageLevel.Green)]
    [DataRow(70, UsageLevel.Orange)]
    [DataRow(84, UsageLevel.Orange)]
    [DataRow(85, UsageLevel.Red)]
    [DataRow(100, UsageLevel.Red)]
    public void DefaultThresholdsAre70And85(int percent, UsageLevel expected)
    {
        Assert.AreEqual(expected, UsageThresholds.Default.LevelOf(percent));
    }

    [TestMethod]
    public void CustomThresholds()
    {
        var thresholds = new UsageThresholds(60, 90);

        Assert.AreEqual(UsageLevel.Green, thresholds.LevelOf(59));
        Assert.AreEqual(UsageLevel.Orange, thresholds.LevelOf(60));
        Assert.AreEqual(UsageLevel.Orange, thresholds.LevelOf(89));
        Assert.AreEqual(UsageLevel.Red, thresholds.LevelOf(90));
    }

    [TestMethod]
    public void TrayIconTextIsTheRamPercent()
    {
        // RAM 20.5 / 31.4 GB 是 65%，commit 31.4 / 39.4 GB 是 80%
        var snapshot = ReportSamples.Typical().Snapshot;

        Assert.AreEqual(65, TrayText.RamPercent(snapshot));
        Assert.AreEqual(80, TrayText.CommitPercent(snapshot));
        Assert.AreEqual("65", TrayText.IconText(snapshot));
    }

    [TestMethod]
    public void TrayIconTextNeverExceeds100()
    {
        var snapshot = ReportSamples.Typical().Snapshot;
        var full = snapshot with { PhysicalAvailableBytes = 0 };
        var over = snapshot with { PhysicalAvailableBytes = -MemoryMath.BytesPerGigabyte };

        Assert.AreEqual("100", TrayText.IconText(full));
        Assert.AreEqual("100", TrayText.IconText(over));
    }

    [TestMethod]
    public void TrayIconColorFollowsTheRamPercent()
    {
        // RAM 65%、commit 80%：依 RAM 是綠色與橘色，依 commit 會是橘色與紅色
        var snapshot = ReportSamples.Typical().Snapshot;

        Assert.AreEqual(UsageLevel.Green, TrayText.IconLevel(snapshot, UsageThresholds.Default));
        Assert.AreEqual(UsageLevel.Orange, TrayText.IconLevel(snapshot, new UsageThresholds(60, 75)));
    }

    [TestMethod]
    public void TooltipShowsRamThenCommit()
    {
        string tooltip = TrayText.Tooltip(ReportSamples.Typical().Snapshot);

        Assert.AreEqual("MemSweep\nRAM 20.5 / 31.4 GB（65%）\nCommit 31.4 / 39.4 GB（80%）", tooltip);
        Assert.IsLessThanOrEqualTo(127, tooltip.Length);
    }

    [TestMethod]
    public void PanelGauges()
    {
        var panel = PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default);

        Assert.AreEqual("Commit", panel.Commit.Label);
        Assert.AreEqual("31.4 / 39.4 GB（80%）", panel.Commit.ValueText);
        Assert.AreEqual(31.4 / 39.4, panel.Commit.Fraction, 0.001);
        Assert.AreEqual(UsageLevel.Orange, panel.Commit.Level);
        Assert.AreEqual("實體 RAM", panel.Ram.Label);
        Assert.AreEqual("20.5 / 31.4 GB（65%）", panel.Ram.ValueText);
        Assert.AreEqual(20.5 / 31.4, panel.Ram.Fraction, 0.001);
        Assert.IsNull(panel.Ram.Level);
    }

    [TestMethod]
    public void PanelRowsAreTopFivePlusUnattributedWithBarsRelativeToTheLargest()
    {
        var panel = PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default);

        Assert.HasCount(6, panel.Rows);
        Assert.AreEqual(new PanelRow("Claude", "36", "3.2 GB", 3.2 / 6.9, false), panel.Rows[0] with { BarFraction = 3.2 / 6.9 });
        Assert.AreEqual(3.2 / 6.9, panel.Rows[0].BarFraction, 0.001);
        Assert.AreEqual("Node.js JavaScript Runtime", panel.Rows[4].Name);
        Assert.AreEqual("30", panel.Rows[4].CountText);
        var last = panel.Rows[5];
        Assert.AreEqual(GroupedSnapshot.UnattributedName, last.Name);
        Assert.AreEqual(string.Empty, last.CountText);
        Assert.AreEqual("6.9 GB", last.GigabytesText);
        Assert.AreEqual(1.0, last.BarFraction, 0.001);
        Assert.IsTrue(last.IsUnattributed);
        Assert.IsFalse(panel.Rows.Take(5).Any(r => r.IsUnattributed));
    }

    [TestMethod]
    public void PanelRowsWithNothingToCompareHaveEmptyBars()
    {
        var typical = ReportSamples.Typical();
        var empty = typical with { Apps = [], UnattributedCommitBytes = 0 };

        var panel = PanelModel.Build(empty, null, UsageThresholds.Default);

        Assert.HasCount(1, panel.Rows);
        Assert.AreEqual(0.0, panel.Rows[0].BarFraction);
    }

    [TestMethod]
    public void PanelCarriesTheStorageWarning()
    {
        var withWarning = PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default, "data 資料夾無法寫入，尖峰紀錄已停用");
        var withoutWarning = PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default);

        Assert.AreEqual("data 資料夾無法寫入，尖峰紀錄已停用", withWarning.Warning);
        Assert.IsNull(withoutWarning.Warning);
    }

    [TestMethod]
    public void PeakTextBeforeTheFirstPeak()
    {
        var panel = PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default);

        Assert.AreEqual("開機以來 commit 尖峰：—", panel.PeakText);
    }

    [TestMethod]
    public void PeakTextWhenTheTimeIsUnknown()
    {
        var peak = new BootPeak(Boot, MemoryMath.FromGigabytes(39.4), MemoryMath.FromGigabytes(39.4), null, null);

        var panel = PanelModel.Build(ReportSamples.Typical(), peak, UsageThresholds.Default);

        Assert.AreEqual("開機以來 commit 尖峰：39.4 GB（時間未知）", panel.PeakText);
    }

    [TestMethod]
    public void PeakTextOnTheSameDayShowsOnlyTheTime()
    {
        var time = new DateTimeOffset(2026, 10, 26, 12, 10, 45, TimeSpan.FromHours(8));

        var panel = PanelModel.Build(ReportSamples.Typical(), KnownPeak(time), UsageThresholds.Default);

        Assert.AreEqual("開機以來 commit 尖峰：36.8 GB（12:10）", panel.PeakText);
    }

    [TestMethod]
    public void PeakTextOnAnEarlierDayShowsTheDate()
    {
        var time = new DateTimeOffset(2026, 10, 25, 21, 14, 0, TimeSpan.FromHours(8));

        var panel = PanelModel.Build(ReportSamples.Typical(), KnownPeak(time), UsageThresholds.Default);

        Assert.AreEqual("開機以來 commit 尖峰：36.8 GB（10/25 21:14）", panel.PeakText);
    }
}
