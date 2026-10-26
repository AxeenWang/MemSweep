namespace MemSweep.Core;

/// <summary>尖峰當下的一個前五名應用程式。</summary>
public sealed record PeakApp(string Name, long CommitBytes);

/// <summary>
/// 本次開機的 commit 尖峰。Time 與 TopApps 為 null 表示尖峰發生在 MemSweep 未執行期間，
/// 發生時間與前五名都無從得知（F-31）。
/// </summary>
public sealed record BootPeak(
    DateTime BootTimeUtc,
    long CommitBytes,
    long CommitLimitBytes,
    DateTimeOffset? Time,
    IReadOnlyList<PeakApp>? TopApps)
{
    public bool IsKnown => Time is not null;
}
