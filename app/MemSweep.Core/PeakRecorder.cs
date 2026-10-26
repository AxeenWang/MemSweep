namespace MemSweep.Core;

/// <summary>交給介面顯示的尖峰狀態；內容不會再被修改，可以交給其他執行緒。</summary>
public sealed record PeakState(BootPeak? BootPeak, PeakHistory History, string? StorageWarning);

/// <summary>
/// 以每次取樣更新尖峰紀錄並寫入 CSV（Design 3.9）：尖峰有變動就標記，最多每 60 秒寫一次，
/// 正常結束時再寫一次；保留天數清理在啟動後第一次取樣與跨日時執行（F-33）。
/// 取樣執行緒與結束時的 UI 執行緒都會呼叫，以 lock 保護。
/// </summary>
public sealed class PeakRecorder
{
    public static readonly TimeSpan WriteInterval = TimeSpan.FromSeconds(60);

    private readonly Lock _gate = new();
    private readonly PeakTracker _tracker;
    private readonly IPeakStore? _store;
    private readonly string? _warning;
    private readonly int _retentionDays;
    private DateOnly? _prunedOn;
    private DateTimeOffset? _lastWrite;
    private bool _dirty;

    public PeakRecorder(PeakStoreOpening opening, int retentionDays)
    {
        _tracker = new PeakTracker(opening.History);
        _store = opening.Store;
        _warning = opening.Warning;
        _retentionDays = retentionDays;
        State = new PeakState(null, opening.History, opening.Warning);
    }

    public PeakState State { get; private set; }

    public void Record(GroupedSnapshot grouped)
    {
        lock (_gate)
        {
            var now = grouped.Snapshot.Timestamp;
            bool changed = _tracker.Update(grouped);
            var today = DateOnly.FromDateTime(now.DateTime);
            if (_prunedOn != today)
            {
                changed |= _tracker.Prune(now, _retentionDays);
                _prunedOn = today;
            }

            // 第一次取樣即使沒有變動（同一次開機內重新啟動），也要讓介面拿到開機尖峰
            if (changed || State.BootPeak is null)
            {
                State = new PeakState(_tracker.BootPeak, _tracker.History, _warning);
            }
            _dirty |= changed;

            // 時鐘往回調時也照常寫入，所以比較時間差的絕對值
            if (_dirty && (_lastWrite is not { } last || (now - last).Duration() >= WriteInterval))
            {
                _lastWrite = now;
                Write();
            }
        }
    }

    /// <summary>正常結束時呼叫：還沒寫入的變動立即寫入。</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (_dirty)
            {
                Write();
            }
        }
    }

    private void Write()
    {
        if (_store is null)
        {
            return;
        }
        try
        {
            _store.Save(_tracker.History);
            _dirty = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 例如使用者用 Excel 開著紀錄檔：保留變動，下一個寫入時機再試
        }
    }
}
