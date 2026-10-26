namespace MemSweep.Core;

/// <summary>
/// 某一天（本機時間）MemSweep 取樣到的 commit 已用最高值與當時的前五名（F-30、F-32）。
/// 只計入 MemSweep 執行期間的取樣，所以時間與前五名一定已知。
/// </summary>
public sealed record DailyPeak(
    DateOnly Date,
    long CommitBytes,
    long CommitLimitBytes,
    DateTimeOffset Time,
    IReadOnlyList<PeakApp> TopApps);
