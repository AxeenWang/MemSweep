using System.Collections.Specialized;
using System.ComponentModel;
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class FullWindowModelTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
    private static readonly DateTime Boot = new(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset PeakTime = new(2026, 10, 26, 14, 35, 2, TimeSpan.FromHours(8));
    private static readonly PeakState NoPeaks = new(null, PeakHistory.Empty, null);

    private static long Gb(double value) => MemoryMath.FromGigabytes(value);

    private static FullWindowModel Model() => new(UsageThresholds.Default, Zone);

    private static AppGroup App(string name, double commitGb, params AppComponent[] components) =>
        new(name, false, Math.Max(1, components.Sum(c => c.ProcessCount)), Gb(commitGb), Gb(commitGb / 2), components);

    private static GroupedSnapshot WithApps(params AppGroup[] apps) => ReportSamples.Typical() with { Apps = apps };

    [TestMethod]
    public void OverviewIsThePanelPlusThePageFileSize()
    {
        var model = Model();

        model.Update(ReportSamples.Typical(), NoPeaks with { StorageWarning = "data 資料夾無法寫入，尖峰紀錄已停用" });

        Assert.AreEqual("31.4 / 39.4 GB（80%）", model.Overview!.Commit.ValueText);
        Assert.HasCount(6, model.Overview.Rows);
        Assert.AreEqual("data 資料夾無法寫入，尖峰紀錄已停用", model.Overview.Warning);
        Assert.AreEqual("分頁檔大小：8.0 GB", model.PageFileText);
    }

    [TestMethod]
    public void NewThresholdsApplyAtTheNextUpdate()
    {
        var model = Model();
        model.Update(ReportSamples.Typical(), NoPeaks);
        Assert.AreEqual(UsageLevel.Green, model.Overview!.Ram.Level);

        model.Thresholds = new UsageThresholds(60, 75);
        model.Update(ReportSamples.Typical(), NoPeaks);

        Assert.AreEqual(UsageLevel.Orange, model.Overview!.Ram.Level);
    }

    [TestMethod]
    public void AppsListsEveryAppByCommitThenUnattributed()
    {
        var model = Model();

        model.Update(ReportSamples.Typical(), NoPeaks);

        Assert.HasCount(7, model.Apps);
        var claude = model.Apps[0];
        Assert.AreEqual("Claude", claude.Name);
        Assert.AreEqual("36", claude.CountText);
        Assert.AreEqual("3.2 GB", claude.CommitText);
        Assert.AreEqual("1.6 GB", claude.WorkingSetText);
        Assert.IsFalse(claude.IsUnattributed);
        Assert.AreEqual("Microsoft Edge", model.Apps[5].Name);
        var last = model.Apps[6];
        Assert.IsTrue(last.IsUnattributed);
        Assert.AreEqual(GroupedSnapshot.UnattributedName, last.Name);
        Assert.AreEqual("6.9 GB", last.CommitText);
        Assert.AreEqual(string.Empty, last.CountText);
        Assert.AreEqual(string.Empty, last.WorkingSetText);
        Assert.IsEmpty(last.Components);
    }

    [TestMethod]
    public void ComponentsShowTheProcessCountAfterTheName()
    {
        var model = Model();
        var claude = App(
            "Claude",
            3.2,
            new AppComponent("claude.exe", 14, Gb(2.0), Gb(1.0)),
            new AppComponent("node：@playwright/mcp", 2, Gb(0.4), Gb(0.3)));

        model.Update(WithApps(claude), NoPeaks);

        CollectionAssert.AreEqual(
            new[]
            {
                new ComponentRow("claude.exe ×14", "2.0 GB", "1.0 GB"),
                new ComponentRow("node：@playwright/mcp ×2", "0.4 GB", "0.3 GB"),
            },
            model.Apps[0].Components.ToArray());
        Assert.AreEqual("16", model.Apps[0].CountText);
    }

    [TestMethod]
    public void RowsAreUpdatedInPlaceAndKeepTheirExpansion()
    {
        var model = Model();
        model.Update(WithApps(App("Claude", 3.2), App("ChatGPT", 2.2)), NoPeaks);
        var claude = model.Apps[0];
        claude.IsExpanded = true;
        var changed = new List<string?>();
        claude.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.Update(WithApps(App("Claude", 3.5), App("ChatGPT", 2.2)), NoPeaks);

        Assert.AreSame(claude, model.Apps[0]);
        Assert.IsTrue(claude.IsExpanded);
        Assert.AreEqual("3.5 GB", claude.CommitText);
        CollectionAssert.AreEquivalent(new[] { "CommitText", "WorkingSetText" }, changed);
    }

    [TestMethod]
    public void ReorderedAppsAreMovedNotRecreated()
    {
        var model = Model();
        model.Update(WithApps(App("Claude", 3.2), App("ChatGPT", 2.2), App("Steam", 1.0)), NoPeaks);
        var chatGpt = model.Apps[1];
        var actions = new List<NotifyCollectionChangedAction>();
        model.Apps.CollectionChanged += (_, e) => actions.Add(e.Action);

        model.Update(WithApps(App("ChatGPT", 4.0), App("Claude", 3.2), App("Steam", 1.0)), NoPeaks);

        Assert.AreSame(chatGpt, model.Apps[0]);
        CollectionAssert.AreEqual(new[] { "ChatGPT", "Claude", "Steam", GroupedSnapshot.UnattributedName }, model.Apps.Select(a => a.Name).ToArray());
        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Move }, actions);
    }

    [TestMethod]
    public void AppsThatAppearOrDisappearAreAddedOrRemoved()
    {
        var model = Model();
        model.Update(WithApps(App("Claude", 3.2), App("Steam", 1.0)), NoPeaks);
        var claude = model.Apps[0];

        model.Update(WithApps(App("Claude", 3.2), App("Game", 2.0)), NoPeaks);

        Assert.AreSame(claude, model.Apps[0]);
        CollectionAssert.AreEqual(new[] { "Claude", "Game", GroupedSnapshot.UnattributedName }, model.Apps.Select(a => a.Name).ToArray());
    }

    [TestMethod]
    public void AppsWithTheSameNameAreKeptApart()
    {
        // 不同路徑的最上層程式可能有相同的檔案描述（R-5 只合併相同路徑）
        var model = Model();
        model.Update(WithApps(App("Node.js JavaScript Runtime", 1.4), App("Node.js JavaScript Runtime", 0.5)), NoPeaks);
        var second = model.Apps[1];
        second.IsExpanded = true;

        model.Update(WithApps(App("Node.js JavaScript Runtime", 1.4), App("Node.js JavaScript Runtime", 0.6)), NoPeaks);

        Assert.HasCount(3, model.Apps);
        Assert.AreSame(second, model.Apps[1]);
        Assert.AreEqual("0.6 GB", model.Apps[1].CommitText);
        Assert.IsFalse(model.Apps[0].IsExpanded);
    }

    [TestMethod]
    public void UnchangedComponentsKeepTheSameList()
    {
        var model = Model();
        var component = new AppComponent("claude.exe", 14, Gb(2.0), Gb(1.0));
        model.Update(WithApps(App("Claude", 3.2, component)), NoPeaks);
        var before = model.Apps[0].Components;

        model.Update(WithApps(App("Claude", 3.2, component)), NoPeaks);

        Assert.AreSame(before, model.Apps[0].Components);
    }

    [TestMethod]
    public void PeakRowsAreNewestFirst()
    {
        var history = new PeakHistory(
            [
                new BootPeak(Boot.AddDays(-1), Gb(39.0), Gb(39.4), null, null),
                new BootPeak(Boot, Gb(36.8), Gb(39.4), PeakTime, [new PeakApp("Claude", Gb(3.2)), new PeakApp("ChatGPT", Gb(2.2))]),
            ],
            [
                new DailyPeak(new DateOnly(2026, 10, 25), Gb(30.5), Gb(39.4), PeakTime.AddDays(-1), [new PeakApp("Steam", Gb(1.0))]),
                new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), PeakTime, [new PeakApp("Claude", Gb(3.2))]),
            ]);
        var model = Model();

        model.Update(ReportSamples.Typical(), new PeakState(history.Boots[1], history, null));

        CollectionAssert.AreEqual(
            new[]
            {
                new PeakRow("2026-10-26 08:30", "2026-10-26 14:35", "36.8 GB", "39.4 GB", "Claude 3.2 GB、ChatGPT 2.2 GB"),
                new PeakRow("2026-10-25 08:30", "未知", "39.0 GB", "39.4 GB", "未知"),
            },
            model.BootPeaks.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                new PeakRow("2026-10-26", "14:35", "31.4 GB", "39.4 GB", "Claude 3.2 GB"),
                new PeakRow("2026-10-25", "14:35", "30.5 GB", "39.4 GB", "Steam 1.0 GB"),
            },
            model.DailyPeaks.ToArray());
    }

    [TestMethod]
    public void PeakRowsAreRebuiltOnlyWhenTheHistoryChanges()
    {
        var history = new PeakHistory([], [new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), PeakTime, [])]);
        var model = Model();
        model.Update(ReportSamples.Typical(), new PeakState(null, history, null));
        var before = model.DailyPeaks;
        var changed = new List<string?>();
        ((INotifyPropertyChanged)model).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.Update(ReportSamples.Typical(), new PeakState(null, history, null));

        Assert.AreSame(before, model.DailyPeaks);
        CollectionAssert.DoesNotContain(changed, nameof(FullWindowModel.DailyPeaks));
        CollectionAssert.DoesNotContain(changed, nameof(FullWindowModel.BootPeaks));
    }
}
