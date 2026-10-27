namespace MemSweep.Core;

/// <summary>
/// 追蹤開機尖峰與每日尖峰（F-30～F-32、Design 3.10），並保存全部尖峰紀錄。
/// 不是執行緒安全的：同一時間只能由一個執行緒呼叫。
/// </summary>
public sealed class PeakTracker
{
    /// <summary>同一次開機的開機時間可能因校時略有偏移，CSV 也只記到秒，差距在這個範圍內就視為同一次開機。</summary>
    public static readonly TimeSpan BootTimeTolerance = TimeSpan.FromSeconds(60);

    // CSV 的 GB 只保留兩位小數，Windows 的尖峰比已有紀錄高出不到半個最小單位時，視為同一個尖峰
    private static readonly long CsvRoundingBytes = MemoryMath.FromGigabytes(0.005);

    private readonly List<BootPeak> _boots;
    private readonly List<DailyPeak> _days;

    public PeakTracker()
        : this(PeakHistory.Empty)
    {
    }

    /// <summary>從已有的紀錄接續，例如 MemSweep 在同一次開機或同一天內重新啟動。</summary>
    public PeakTracker(PeakHistory saved)
    {
        _boots = saved.Boots.ToList();
        _days = saved.Days.ToList();
        History = saved;
    }

    /// <summary>本次開機的尖峰；第一次取樣前為 null。</summary>
    public BootPeak? BootPeak { get; private set; }

    /// <summary>最近一次取樣那一天的尖峰；第一次取樣前為 null。</summary>
    public DailyPeak? TodayPeak { get; private set; }

    /// <summary>全部尖峰紀錄；有變動時換成新的物件，舊物件不會被修改。</summary>
    public PeakHistory History { get; private set; }

    /// <summary>以一次取樣更新開機尖峰與每日尖峰；任一筆紀錄有變動時回傳 true。</summary>
    public bool Update(GroupedSnapshot grouped)
    {
        bool bootChanged = UpdateBoot(grouped);
        bool dayChanged = UpdateDay(grouped);
        if (!bootChanged && !dayChanged)
        {
            return false;
        }
        Publish();
        return true;
    }

    /// <summary>
    /// 刪除早於保留天數的紀錄：保留今天在內的 retentionDays 天（F-33）。
    /// 開機紀錄以尖峰時間的日期判斷，時間未知時用開機時間；本次開機與今天的紀錄一律保留。
    /// 有刪除時回傳 true。
    /// </summary>
    public bool Prune(DateTimeOffset now, int retentionDays)
    {
        var oldestKept = DateOnly.FromDateTime(now.DateTime).AddDays(1 - retentionDays);
        int removed = _boots.RemoveAll(b => !ReferenceEquals(b, BootPeak) && DateOf(b, now.Offset) < oldestKept)
            + _days.RemoveAll(d => !ReferenceEquals(d, TodayPeak) && d.Date < oldestKept);
        if (removed == 0)
        {
            return false;
        }
        Publish();
        return true;
    }

    private bool UpdateBoot(GroupedSnapshot grouped)
    {
        var snapshot = grouped.Snapshot;
        if (BootPeak is null)
        {
            // 第一次取樣：MemSweep 在同一次開機內重新啟動時，接續已有的紀錄
            var saved = _boots.LastOrDefault(b => (b.BootTimeUtc - snapshot.BootTimeUtc).Duration() <= BootTimeTolerance);
            if (saved is not null && snapshot.CommitPeakBytes <= saved.CommitBytes + CsvRoundingBytes)
            {
                // 沿用已有紀錄的時間與前五名；數值改用較高的 Windows 精確值，之後的取樣才比較得準
                var resumed = snapshot.CommitPeakBytes > saved.CommitBytes
                    ? saved with { CommitBytes = snapshot.CommitPeakBytes }
                    : saved;
                Replace(_boots, saved, resumed);
                BootPeak = resumed;
                return !ReferenceEquals(resumed, saved);
            }

            // Windows 的尖峰高於目前用量，代表尖峰發生在 MemSweep 未執行期間（F-31）
            var first = snapshot.CommitPeakBytes > snapshot.CommitUsedBytes
                ? new BootPeak(snapshot.BootTimeUtc, snapshot.CommitPeakBytes, snapshot.CommitLimitBytes, null, null)
                : BootFromSample(grouped);
            Replace(_boots, saved, first);
            BootPeak = first;
            return true;
        }

        if (snapshot.CommitPeakBytes <= BootPeak.CommitBytes)
        {
            return false;
        }

        // 尖峰發生在上次取樣之後（最多 2 秒前），以本次取樣的時間與前五名作為近似值
        var next = BootFromSample(grouped);
        Replace(_boots, BootPeak, next);
        BootPeak = next;
        return true;
    }

    private bool UpdateDay(GroupedSnapshot grouped)
    {
        var snapshot = grouped.Snapshot;
        var date = DateOnly.FromDateTime(snapshot.Timestamp.DateTime);
        if (TodayPeak?.Date != date)
        {
            // 第一次取樣或跨過午夜（F-32）：同一天已有紀錄時（MemSweep 重新啟動）就接續
            TodayPeak = _days.LastOrDefault(d => d.Date == date);
        }

        if (TodayPeak is not null && snapshot.CommitUsedBytes <= TodayPeak.CommitBytes)
        {
            return false;
        }

        var next = new DailyPeak(date, snapshot.CommitUsedBytes, snapshot.CommitLimitBytes, snapshot.Timestamp, TopApps(grouped));
        Replace(_days, TodayPeak, next);
        TodayPeak = next;
        return true;
    }

    private void Publish() => History = new PeakHistory(_boots.ToList(), _days.ToList());

    private static void Replace<T>(List<T> list, T? current, T next)
        where T : class
    {
        int index = current is null ? -1 : list.FindIndex(item => ReferenceEquals(item, current));
        if (index < 0)
        {
            list.Add(next);
        }
        else
        {
            list[index] = next;
        }
    }

    private static DateOnly DateOf(BootPeak boot, TimeSpan localOffset) =>
        DateOnly.FromDateTime((boot.Time ?? new DateTimeOffset(boot.BootTimeUtc).ToOffset(localOffset)).DateTime);

    private static BootPeak BootFromSample(GroupedSnapshot grouped)
    {
        var snapshot = grouped.Snapshot;
        return new BootPeak(
            snapshot.BootTimeUtc,
            snapshot.CommitPeakBytes,
            snapshot.CommitLimitBytes,
            snapshot.Timestamp,
            TopApps(grouped));
    }

    private static List<PeakApp> TopApps(GroupedSnapshot grouped) =>
        grouped.Apps
            .Take(ReportFormatter.TopCount)
            .Select(a => new PeakApp(a.Name, a.CommitBytes))
            .ToList();
}
