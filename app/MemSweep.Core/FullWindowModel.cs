using System.Collections.ObjectModel;
using System.Globalization;

namespace MemSweep.Core;

/// <summary>「尖峰紀錄」分頁的一列（F-24）；開機尖峰的 First 是開機時間，每日尖峰的是日期。</summary>
public sealed record PeakRow(string FirstText, string TimeText, string CommitText, string LimitText, string TopAppsText);

/// <summary>完整視窗的顯示資料（F-20～F-24）：總覽、程式、尖峰紀錄三個分頁。</summary>
public sealed class FullWindowModel : ObservableObject
{
    private const string UnattributedKey = "#unattributed";

    private readonly TimeZoneInfo _zone;
    private PanelModel? _overview;
    private string _pageFileText = string.Empty;
    private PeakHistory? _history;
    private IReadOnlyList<PeakRow> _bootPeaks = [];
    private IReadOnlyList<PeakRow> _dailyPeaks = [];

    public FullWindowModel(UsageThresholds thresholds, TimeZoneInfo zone)
    {
        Thresholds = thresholds;
        _zone = zone;
    }

    /// <summary>RAM 量表的顏色門檻；設定變更後從下一次 Update 開始套用。</summary>
    public UsageThresholds Thresholds { get; set; }

    /// <summary>總覽：小面板的全部資訊（F-22）。</summary>
    public PanelModel? Overview { get => _overview; private set => Set(ref _overview, value); }

    /// <summary>總覽另外顯示的分頁檔大小（F-22）。</summary>
    public string PageFileText { get => _pageFileText; private set => Set(ref _pageFileText, value); }

    /// <summary>所有應用程式依 commit 由大到小，最後一列是「系統（不屬於任何程式）」（F-23）。</summary>
    public ObservableCollection<AppListRow> Apps { get; } = [];

    /// <summary>開機尖峰，新的在前。</summary>
    public IReadOnlyList<PeakRow> BootPeaks { get => _bootPeaks; private set => Set(ref _bootPeaks, value); }

    /// <summary>每日尖峰，新的在前。</summary>
    public IReadOnlyList<PeakRow> DailyPeaks { get => _dailyPeaks; private set => Set(ref _dailyPeaks, value); }

    public void Update(GroupedSnapshot grouped, PeakState peaks)
    {
        Overview = PanelModel.Build(grouped, peaks.BootPeak, Thresholds, peaks.StorageWarning);
        PageFileText = "分頁檔大小：" + Gigabytes(grouped.Snapshot.PageFileTotalBytes);
        UpdateApps(grouped);

        // 尖峰紀錄只在有變動時（換成新的 PeakHistory）才重建
        if (!ReferenceEquals(peaks.History, _history))
        {
            _history = peaks.History;
            BootPeaks = peaks.History.Boots.OrderByDescending(b => b.BootTimeUtc).Select(BootRow).ToList();
            DailyPeaks = peaks.History.Days.OrderByDescending(d => d.Date).Select(DayRow).ToList();
        }
    }

    internal static string Gigabytes(long bytes) => MemoryMath.FormatGigabytes(bytes) + " GB";

    private void UpdateApps(GroupedSnapshot grouped)
    {
        int index = 0;
        foreach (var app in grouped.Apps)
        {
            Place(index++, app.Name, false).Update(app);
        }
        Place(index++, UnattributedKey, true).UpdateUnattributed(grouped.UnattributedCommitBytes);

        // 這次沒有出現的列都被擠到最後面
        while (Apps.Count > index)
        {
            Apps.RemoveAt(Apps.Count - 1);
        }
    }

    // 把鍵相同的列移到 index；沒有就新增一列。從 index 往後找，所以同名的列依出現順序對應
    private AppListRow Place(int index, string key, bool isUnattributed)
    {
        for (int i = index; i < Apps.Count; i++)
        {
            if (Apps[i].Key == key)
            {
                if (i != index)
                {
                    Apps.Move(i, index);
                }
                return Apps[index];
            }
        }
        var row = new AppListRow(key, isUnattributed);
        Apps.Insert(index, row);
        return row;
    }

    private PeakRow BootRow(BootPeak boot) =>
        new(
            TimeZoneInfo.ConvertTimeFromUtc(boot.BootTimeUtc, _zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            boot.Time is { } time ? Local(time).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : PeakCsv.Unknown,
            Gigabytes(boot.CommitBytes),
            Gigabytes(boot.CommitLimitBytes),
            boot.TopApps is { } apps ? TopAppsText(apps) : PeakCsv.Unknown);

    private PeakRow DayRow(DailyPeak day) =>
        new(
            day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Local(day.Time).ToString("HH:mm", CultureInfo.InvariantCulture),
            Gigabytes(day.CommitBytes),
            Gigabytes(day.CommitLimitBytes),
            TopAppsText(day.TopApps));

    private DateTimeOffset Local(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, _zone);

    private static string TopAppsText(IReadOnlyList<PeakApp> apps) =>
        string.Join("、", apps.Select(a => a.Name + " " + Gigabytes(a.CommitBytes)));
}
