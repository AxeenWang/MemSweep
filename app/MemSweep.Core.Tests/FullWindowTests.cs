using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using MemSweep.Core;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

// 多個 STA 執行緒同時初始化 WPF 介面元件時會互相鎖死，介面測試一律依序執行
[TestClass]
[DoNotParallelize]
public sealed class FullWindowTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
    private static readonly PeakState NoPeaks = new(null, PeakHistory.Empty, null);

    private static long Gb(double value) => MemoryMath.FromGigabytes(value);

    private static FullWindowModel ModelWithComponents()
    {
        var typical = ReportSamples.Typical();
        var claude = typical.Apps[0] with
        {
            Components = [new AppComponent("claude.exe", 14, Gb(2.0), Gb(1.0)), new AppComponent("node：@playwright/mcp", 2, Gb(0.4), Gb(0.3))],
        };
        var model = new FullWindowModel(UsageThresholds.Default, Zone);
        model.Update(typical with { Apps = [claude, .. typical.Apps.Skip(1)] }, NoPeaks);
        return model;
    }

    [STATestMethod]
    public void AppsViewListsEveryAppUnderTheColumnHeaders()
    {
        var view = WpfLayout.LaidOut(new AppsView { DataContext = ModelWithComponents() }, 700);

        var texts = WpfLayout.Texts(view);
        CollectionAssert.IsSubsetOf(
            new[] { "名稱", "行程數", "Commit", "實際 RAM", "Claude", "36", "3.2 GB", "1.6 GB", "Microsoft Edge", GroupedSnapshot.UnattributedName, "6.9 GB" },
            texts);
        CollectionAssert.DoesNotContain(texts, "claude.exe ×14");
    }

    [STATestMethod]
    public void TheToggleExpandsTheRowToShowItsComponents()
    {
        var model = ModelWithComponents();
        var view = WpfLayout.LaidOut(new AppsView { DataContext = model }, 700);

        WpfLayout.Descendants(view).OfType<ToggleButton>().First().IsChecked = true;
        view.UpdateLayout();

        Assert.IsTrue(model.Apps[0].IsExpanded);
        CollectionAssert.IsSubsetOf(new[] { "claude.exe ×14", "2.0 GB", "1.0 GB", "node：@playwright/mcp ×2" }, WpfLayout.Texts(view));
    }

    [STATestMethod]
    public void RowsWithoutComponentsHaveNoToggle()
    {
        var view = WpfLayout.LaidOut(new AppsView { DataContext = ModelWithComponents() }, 700);

        var toggles = WpfLayout.Descendants(view).OfType<ToggleButton>().ToList();

        Assert.HasCount(7, toggles);
        Assert.AreEqual(Visibility.Visible, toggles[0].Visibility);
        Assert.AreEqual(Visibility.Hidden, toggles[6].Visibility);
    }

    [STATestMethod]
    public void PeaksViewShowsEmptyTextWithoutRecords()
    {
        var model = new FullWindowModel(UsageThresholds.Default, Zone);
        model.Update(ReportSamples.Typical(), NoPeaks);

        var view = WpfLayout.LaidOut(new PeaksView { DataContext = model }, 700);

        Assert.AreEqual(Visibility.Visible, view.BootEmpty.Visibility);
        Assert.AreEqual(Visibility.Visible, view.DailyEmpty.Visibility);
        Assert.AreEqual("尚無紀錄", view.BootEmpty.Text);
    }

    [STATestMethod]
    public void PeaksViewListsBootAndDailyPeaks()
    {
        var time = new DateTimeOffset(2026, 10, 26, 14, 35, 2, TimeSpan.FromHours(8));
        var history = new PeakHistory(
            [new BootPeak(new DateTime(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc), Gb(36.8), Gb(39.4), null, null)],
            [new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), time, [new PeakApp("Claude", Gb(3.2))])]);
        var model = new FullWindowModel(UsageThresholds.Default, Zone);
        model.Update(ReportSamples.Typical(), new PeakState(history.Boots[0], history, null));

        var view = WpfLayout.LaidOut(new PeaksView { DataContext = model }, 700);

        Assert.AreEqual(Visibility.Collapsed, view.BootEmpty.Visibility);
        Assert.AreEqual(Visibility.Collapsed, view.DailyEmpty.Visibility);
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "開機尖峰", "開機時間", "每日尖峰", "日期", "當時的前五名",
                "2026-10-26 08:30", "未知", "36.8 GB", "39.4 GB",
                "2026-10-26", "14:35", "31.4 GB", "Claude 3.2 GB",
            },
            WpfLayout.Texts(view));
    }

    [STATestMethod]
    public void SummaryUsesOneTimeBindingsAndFollowsANewDataContext()
    {
        var typical = ReportSamples.Typical();
        var summary = WpfLayout.LaidOut(
            new SummaryView { DataContext = PanelModel.Build(typical, null, UsageThresholds.Default, "data 資料夾無法寫入，尖峰紀錄已停用") },
            330);

        var bound = WpfLayout.Descendants(summary)
            .OfType<TextBlock>()
            .Select(t => BindingOperations.GetBinding(t, TextBlock.TextProperty))
            .OfType<Binding>()
            .ToList();
        Assert.IsGreaterThan(10, bound.Count);
        Assert.IsTrue(bound.All(b => b.Mode == BindingMode.OneTime));

        var lower = typical with { Snapshot = typical.Snapshot with { CommitUsedBytes = Gb(20.0) } };
        summary.DataContext = PanelModel.Build(lower, null, UsageThresholds.Default);
        summary.UpdateLayout();

        Assert.AreEqual(Visibility.Collapsed, summary.WarningText.Visibility);
        CollectionAssert.Contains(WpfLayout.Texts(summary), "20.0 / 39.4 GB（51%）");
    }

    [STATestMethod]
    public void TheOverviewTabShowsTheSummaryAndThePageFileSize()
    {
        var model = ModelWithComponents();
        var window = new FullWindow { DataContext = model };

        var content = WpfLayout.LaidOut((FrameworkElement)window.Content, 700);

        var texts = WpfLayout.Texts(content);
        CollectionAssert.IsSubsetOf(new[] { "總覽", "程式", "尖峰紀錄", "Commit", "31.4 / 39.4 GB（80%）", "分頁檔大小：8.0 GB" }, texts);
        window.Close();
    }
}
