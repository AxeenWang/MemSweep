namespace MemSweep.Core;

/// <summary>一次取樣得到的系統記憶體狀態與所有行程。</summary>
/// <param name="Timestamp">取樣時間（本機時區）。</param>
/// <param name="BootTimeUtc">本次開機時間（UTC）。</param>
/// <param name="CommitUsedBytes">commit 已用。</param>
/// <param name="CommitLimitBytes">commit 上限。</param>
/// <param name="CommitPeakBytes">Windows 記錄的開機以來 commit 尖峰。</param>
/// <param name="PhysicalTotalBytes">實體 RAM 總量。</param>
/// <param name="PhysicalAvailableBytes">實體 RAM 可用量。</param>
/// <param name="PageFileTotalBytes">所有分頁檔目前大小的總和。</param>
/// <param name="Processes">所有行程。</param>
public sealed record SystemSnapshot(
    DateTimeOffset Timestamp,
    DateTime BootTimeUtc,
    long CommitUsedBytes,
    long CommitLimitBytes,
    long CommitPeakBytes,
    long PhysicalTotalBytes,
    long PhysicalAvailableBytes,
    long PageFileTotalBytes,
    IReadOnlyList<ProcessSample> Processes)
{
    public long CommitFreeBytes => CommitLimitBytes - CommitUsedBytes;

    public long PhysicalUsedBytes => PhysicalTotalBytes - PhysicalAvailableBytes;
}
