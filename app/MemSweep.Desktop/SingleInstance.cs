using MemSweep.Desktop.Native;

namespace MemSweep.Desktop;

/// <summary>
/// 單一執行個體（F-60、Design 3.7）：第一個執行個體建立具名 Mutex 與事件；
/// 之後啟動的執行個體設定事件請它顯示小面板，自己隨即結束。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\MemSweep.SingleInstance";
    public const string ShowPanelEventName = @"Local\MemSweep.ShowPanel";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showPanel;
    private readonly RegisteredWaitHandle _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle showPanel)
    {
        _mutex = mutex;
        _showPanel = showPanel;
        _registration = ThreadPool.RegisterWaitForSingleObject(
            showPanel, (_, _) => ShowRequested?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>其他執行個體要求顯示小面板；在執行緒集區上觸發。</summary>
    public event Action? ShowRequested;

    /// <summary>取得執行個體；回傳 null 表示已有執行個體在執行，並已通知它顯示小面板。</summary>
    public static SingleInstance? TryAcquire(string mutexName, string showPanelEventName)
    {
        // 只用 Mutex 是否已存在來判斷，不持有所有權，所以不必在建立它的執行緒上釋放
        var mutex = new Mutex(initiallyOwned: false, mutexName, out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            if (EventWaitHandle.TryOpenExisting(showPanelEventName, out var existing))
            {
                using (existing)
                {
                    // 使用者剛啟動的這個行程在前景，把前景許可讓給第一個執行個體，小面板才拿得到焦點
                    User32.AllowSetForegroundWindow(User32.AsfwAny);
                    existing.Set();
                }
            }
            return null;
        }

        return new SingleInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, showPanelEventName));
    }

    public void Dispose()
    {
        _registration.Unregister(null);
        _showPanel.Dispose();
        _mutex.Dispose();
    }
}
