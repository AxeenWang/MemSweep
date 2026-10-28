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

    // 文字所在的欄；畫面上的左右順序看欄，不看元素的先後
    private static int ColumnOf(DependencyObject root, string text) =>
        Grid.GetColumn(WpfLayout.Descendants(root).OfType<TextBlock>().Single(t => t.Text == text));

    [STATestMethod]
    public void AppsViewListsEveryAppUnderTheColumnHeaders()
    {
        var view = WpfLayout.LaidOut(new AppsView { DataContext = ModelWithComponents() }, 700);

        var texts = WpfLayout.Texts(view);
        CollectionAssert.IsSubsetOf(
            new[] { "名稱", "行程數", "RAM", "Commit", "Claude", "36", "1.6 GB", "3.2 GB", "Microsoft Edge", GroupedSnapshot.UnattributedName, "12.3 GB", "6.9 GB" },
            texts);
        CollectionAssert.DoesNotContain(texts, "claude.exe ×14");
        // RAM 欄在 commit 欄之前：標題與 Claude 那一列都是
        Assert.IsLessThan(ColumnOf(view, "Commit"), ColumnOf(view, "RAM"));
        Assert.IsLessThan(ColumnOf(view, "3.2 GB"), ColumnOf(view, "1.6 GB"));
    }

    [STATestMethod]
    public void TheToggleExpandsTheRowToShowItsComponents()
    {
        var model = ModelWithComponents();
        var view = WpfLayout.LaidOut(new AppsView { DataContext = model }, 700);
        var claude = model.Apps.Single(a => a.Name == "Claude");

        WpfLayout.Descendants(view).OfType<ToggleButton>().Single(t => t.DataContext == claude).IsChecked = true;
        view.UpdateLayout();

        Assert.IsTrue(claude.IsExpanded);
        var texts = WpfLayout.Texts(view);
        CollectionAssert.IsSubsetOf(new[] { "claude.exe ×14", "1.0 GB", "2.0 GB", "node：@playwright/mcp ×2" }, texts);
        Assert.IsLessThan(ColumnOf(view, "2.0 GB"), ColumnOf(view, "1.0 GB"));
    }

    [STATestMethod]
    public void RowsWithoutComponentsHaveNoToggle()
    {
        var view = WpfLayout.LaidOut(new AppsView { DataContext = ModelWithComponents() }, 700);

        var toggles = WpfLayout.Descendants(view).OfType<ToggleButton>().ToList();

        // 依 RAM 排序後 Claude 是第二列，只有它有明細
        Assert.HasCount(7, toggles);
        Assert.AreEqual(Visibility.Hidden, toggles[0].Visibility);
        Assert.AreEqual(Visibility.Visible, toggles[1].Visibility);
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

    private static FullWindowModel ModelWithPeaks()
    {
        PeakApp[] top =
        [
            new("Claude", Gb(3.2)), new("ChatGPT", Gb(2.2)), new("Microsoft Edge WebView2", Gb(1.8)),
            new("Node.js JavaScript Runtime", Gb(1.4)), new("Microsoft Edge", Gb(1.3)),
        ];
        var time = new DateTimeOffset(2026, 10, 26, 14, 35, 2, TimeSpan.FromHours(8));
        var boot = new BootPeak(new DateTime(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc), Gb(36.8), Gb(39.4), time, top);
        var history = new PeakHistory([boot], [new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), time, top)]);
        var model = new FullWindowModel(UsageThresholds.Default, Zone);
        model.Update(ReportSamples.Typical(), new PeakState(boot, history, null));
        return model;
    }

    // 把完整視窗的內容排成指定的視窗大小，並切到指定的分頁
    private static FrameworkElement WindowContent(FullWindow window, double width, double height, int tab)
    {
        var content = WpfLayout.Sized((FrameworkElement)window.Content, width, height);
        WpfLayout.Descendants(content).OfType<TabControl>().Single().SelectedIndex = tab;
        content.UpdateLayout();
        return content;
    }

    [STATestMethod]
    [DataRow(520, 360)]
    [DataRow(760, 560)]
    [DataRow(1280, 800)]
    public void TheOverviewFillsTheWindowWidthLeavingRoomForTheScrollBar(int width, int height)
    {
        var window = new FullWindow { DataContext = ModelWithComponents() };
        var content = WindowContent(window, width, height, 0);

        var summary = WpfLayout.Descendants(content).OfType<SummaryView>().Single();
        var scroller = WpfLayout.Ancestor<ScrollViewer>(summary);
        double right = summary.TranslatePoint(new Point(summary.ActualWidth, 0), scroller).X;

        Assert.AreEqual(scroller.ViewportWidth - 8, right, 0.5);
        window.Close();
    }

    [STATestMethod]
    public void TheTopAppsHeaderIsFullyVisibleInTheSmallestWindow()
    {
        var window = new FullWindow { DataContext = ModelWithPeaks() };
        var content = WindowContent(window, window.MinWidth, window.MinHeight, 2);

        var peaks = WpfLayout.Descendants(content).OfType<PeaksView>().Single();
        var headers = WpfLayout.Descendants(peaks).OfType<TextBlock>().Where(t => t.Text == "當時的前五名").ToList();

        // 欄位比文字窄時 TextBlock 仍保留原本的寬度、只是畫面被裁掉，所以比的是欄位扣掉左邊距後的寬度
        Assert.HasCount(2, headers);
        foreach (var header in headers)
        {
            var natural = new TextBlock { Text = header.Text, FontFamily = header.FontFamily, FontSize = header.FontSize, FontWeight = header.FontWeight };
            natural.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double room = ((Grid)header.Parent).ColumnDefinitions[Grid.GetColumn(header)].ActualWidth - header.Margin.Left;
            Assert.IsGreaterThanOrEqualTo(natural.DesiredSize.Width, room);
        }
        window.Close();
    }

    [STATestMethod]
    [DataRow(520, 360)]
    [DataRow(1280, 800)]
    public void PeakColumnsLineUpWithTheirHeaders(int width, int height)
    {
        var window = new FullWindow { DataContext = ModelWithPeaks() };
        var content = WindowContent(window, width, height, 2);

        // 欄名與每一列各是一個 Grid；兩段（開機、每日）的欄位起點都要一致
        var peaks = WpfLayout.Descendants(content).OfType<PeaksView>().Single();
        var offsets = WpfLayout.Descendants(peaks)
            .OfType<Grid>()
            .Where(g => g.ColumnDefinitions.Count == 5)
            .Select(g => string.Join(",", g.ColumnDefinitions.Select(c => g.TranslatePoint(new Point(c.Offset, 0), peaks).X.ToString("F1"))))
            .ToList();

        Assert.HasCount(4, offsets);
        Assert.AreEqual(1, offsets.Distinct().Count(), string.Join(" | ", offsets));
        window.Close();
    }
}
