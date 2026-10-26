namespace MemSweep.Core;

/// <summary>
/// 追蹤本次開機的 commit 尖峰（Design 3.10）。Phase 02 只保存在記憶體中；
/// 只能由取樣的那一個執行緒呼叫 <see cref="Update"/>。
/// </summary>
public sealed class PeakTracker
{
    public BootPeak? BootPeak { get; private set; }

    /// <summary>以一次取樣更新開機尖峰；尖峰有變動時回傳 true。</summary>
    public bool Update(GroupedSnapshot grouped)
    {
        var snapshot = grouped.Snapshot;
        if (BootPeak is null)
        {
            // 第一次取樣：Windows 記錄的尖峰高於目前用量，代表尖峰發生在 MemSweep 執行之前
            BootPeak = snapshot.CommitPeakBytes > snapshot.CommitUsedBytes
                ? new BootPeak(snapshot.BootTimeUtc, snapshot.CommitPeakBytes, snapshot.CommitLimitBytes, null, null)
                : FromSample(grouped);
            return true;
        }

        if (snapshot.CommitPeakBytes <= BootPeak.CommitBytes)
        {
            return false;
        }

        // 尖峰發生在上次取樣之後（最多 2 秒前），以本次取樣的時間與前五名作為近似值
        BootPeak = FromSample(grouped);
        return true;
    }

    private static BootPeak FromSample(GroupedSnapshot grouped)
    {
        var snapshot = grouped.Snapshot;
        return new BootPeak(
            snapshot.BootTimeUtc,
            snapshot.CommitPeakBytes,
            snapshot.CommitLimitBytes,
            snapshot.Timestamp,
            grouped.Apps
                .Take(ReportFormatter.TopCount)
                .Select(a => new PeakApp(a.Name, a.CommitBytes))
                .ToList());
    }
}
