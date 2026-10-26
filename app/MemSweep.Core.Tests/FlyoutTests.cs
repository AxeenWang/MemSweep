using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MemSweep.Core;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class FlyoutTests
{
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static FlyoutPanel LaidOutPanel(PanelModel model)
    {
        var panel = new FlyoutPanel { DataContext = model };
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        panel.Arrange(new Rect(panel.DesiredSize));
        panel.UpdateLayout();
        return panel;
    }

    [TestMethod]
    public void PlacesTheFlyoutAtTheBottomRightOfTheWorkArea()
    {
        var (left, top) = FlyoutPlacement.Place(areaRight: 1920, areaBottom: 1032, width: 330, height: 400);

        Assert.AreEqual(1578.0, left);
        Assert.AreEqual(620.0, top);
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

        var texts = Descendants(panel).OfType<TextBlock>().Select(t => t.Text).ToList();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "Commit",
                "31.4 / 39.4 GB（80%）",
                "實體 RAM",
                "20.5 / 31.4 GB（65%）",
                "開機以來 commit 尖峰：36.8 GB（12:10）",
                "Claude",
                "36",
                "3.2 GB",
                "系統（不屬於任何程式）",
                "6.9 GB",
            },
            texts);
        var buttons = Descendants(panel).OfType<Button>().Select(b => b.Content as string).ToList();
        CollectionAssert.AreEqual(new[] { "複製報告", "結束 MemSweep" }, buttons);
    }

    [STATestMethod]
    public void PanelShowsTheStorageWarningOnlyWhenThereIsOne()
    {
        const string warning = "peaks-daily.csv 無法讀取，尖峰紀錄已停用";
        var withWarning = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default, warning));
        var withoutWarning = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default));

        Assert.AreEqual(Visibility.Visible, withWarning.WarningText.Visibility);
        Assert.AreEqual(warning, withWarning.WarningText.Text);
        Assert.AreEqual(Visibility.Collapsed, withoutWarning.WarningText.Visibility);
    }

    [STATestMethod]
    public void PanelListsTopFivePlusUnattributed()
    {
        var panel = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default));

        var rows = Descendants(panel).OfType<ContentPresenter>().Where(c => c.Content is PanelRow).ToList();

        Assert.HasCount(6, rows);
    }
}
