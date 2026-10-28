using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MemSweep.Core;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

// 多個 STA 執行緒同時初始化 WPF 介面元件時會互相鎖死，介面測試一律依序執行
[TestClass]
[DoNotParallelize]
public sealed class FlyoutTests
{
    private static FlyoutPanel LaidOutPanel(PanelModel model) => WpfLayout.LaidOut(new FlyoutPanel { DataContext = model });

    [TestMethod]
    public void PlacesTheFlyoutAtTheBottomRightOfTheWorkArea()
    {
        var (left, top) = FlyoutPlacement.Place(areaRight: 1920, areaBottom: 1032, width: 330, height: 400);

        Assert.AreEqual(1578.0, left);
        Assert.AreEqual(620.0, top);
    }

    [TestMethod]
    public void AnInactivePanelHidesWhenTheForegroundWindowChanges()
    {
        Assert.AreEqual(InactiveCheck.KeepWatching, FlyoutPlacement.Next(isActive: false, foregroundAtShow: 100, foregroundNow: 100));
        Assert.AreEqual(InactiveCheck.Hide, FlyoutPlacement.Next(isActive: false, foregroundAtShow: 100, foregroundNow: 200));
        Assert.AreEqual(InactiveCheck.StopWatching, FlyoutPlacement.Next(isActive: true, foregroundAtShow: 100, foregroundNow: 200));
    }

    [TestMethod]
    public void ThemeFollowsAppsUseLightTheme()
    {
        Assert.AreEqual(ThemeKind.Dark, ThemeReader.FromRegistryValue(0));
        Assert.AreEqual(ThemeKind.Light, ThemeReader.FromRegistryValue(1));
    }

    [TestMethod]
    public void MissingOrUnexpectedThemeValueIsLight()
    {
        Assert.AreEqual(ThemeKind.Light, ThemeReader.FromRegistryValue(null));
        Assert.AreEqual(ThemeKind.Light, ThemeReader.FromRegistryValue("0"));
    }

    [STATestMethod]
    public void PanelShowsGaugesPeakRowsAndButtons()
    {
        var peak = new BootPeak(
            new DateTime(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc),
            MemoryMath.FromGigabytes(36.8),
            MemoryMath.FromGigabytes(39.4),
            new DateTimeOffset(2026, 10, 26, 12, 10, 0, TimeSpan.FromHours(8)),
            [new PeakApp("Claude", 1)]);
        var panel = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), peak, UsageThresholds.Default));

        var texts = WpfLayout.Descendants(panel).OfType<TextBlock>().Select(t => t.Text).ToList();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "實體 RAM",
                "20.5 / 31.4 GB（65%）",
                "Commit",
                "31.4 / 39.4 GB（80%）",
                "開機以來 commit 尖峰：36.8 GB（12:10）",
                "Claude",
                "36",
                "1.6 GB",
                "系統（不屬於任何程式）",
                "12.3 GB",
            },
            texts);
        var buttons = WpfLayout.Descendants(panel).OfType<Button>().Select(b => b.Content as string).ToList();
        CollectionAssert.AreEqual(new[] { "完整清單…", "複製報告", "結束 MemSweep" }, buttons);
    }

    [STATestMethod]
    public void PanelShowsTheStorageWarningOnlyWhenThereIsOne()
    {
        const string warning = "peaks-daily.csv 無法讀取，尖峰紀錄已停用";
        var withWarning = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default, warning));
        var withoutWarning = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default));

        Assert.AreEqual(Visibility.Visible, withWarning.Summary.WarningText.Visibility);
        Assert.AreEqual(warning, withWarning.Summary.WarningText.Text);
        Assert.AreEqual(Visibility.Collapsed, withoutWarning.Summary.WarningText.Visibility);
    }

    [STATestMethod]
    public void PanelListsTopFivePlusUnattributed()
    {
        var panel = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default));

        var rows = WpfLayout.Descendants(panel).OfType<ContentPresenter>().Where(c => c.Content is PanelRow).ToList();

        Assert.HasCount(6, rows);
    }

    [STATestMethod]
    public void TheRamGaugeComesFirstAndIsTheOneColoredByTheThresholds()
    {
        var panel = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default));

        var texts = WpfLayout.Texts(panel.Summary);
        Assert.IsLessThan(texts.IndexOf("Commit"), texts.IndexOf("實體 RAM"));
        // 只有一個量表依門檻變色，而且看的是 RAM
        var colored = WpfLayout.Descendants(panel.Summary).OfType<Border>().Single(b => b.Style is { Triggers.Count: > 0 });
        var paths = colored.Style.Triggers.OfType<DataTrigger>().Select(t => ((Binding)t.Binding).Path.Path).Distinct().ToArray();
        CollectionAssert.AreEqual(new[] { "Ram.Level" }, paths);
    }
}
