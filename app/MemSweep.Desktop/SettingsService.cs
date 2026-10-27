using System.IO;
using System.Security;
using MemSweep.Core;

namespace MemSweep.Desktop;

/// <summary>
/// 目前的設定（F-50）：建立時讀取 settings.json；儲存時先寫入檔案，成功後才套用新設定與開機自動啟動
/// （F-51、Design 3.8）。必須在 UI 執行緒上使用。
/// </summary>
internal sealed class SettingsService
{
    public const string SaveFailedMessage = "無法寫入 settings.json，設定沒有儲存";
    public const string AutoStartFailedMessage = "設定已儲存，但無法變更開機自動啟動";

    private readonly SettingsStore _store;
    private readonly AutoStart _autoStart;

    public SettingsService(SettingsStore store, AutoStart autoStart)
    {
        _store = store;
        _autoStart = autoStart;
        Current = store.Load();
    }

    public AppSettings Current { get; private set; }

    /// <summary>儲存成功後觸發，讓系統匣、視窗與尖峰紀錄套用新設定。</summary>
    public event Action<AppSettings>? Changed;

    /// <summary>設定為開啟時，每次啟動都檢查登記的路徑，exe 被移動過就更新（Design 3.8）；失敗時下次啟動再試。</summary>
    public void SyncAutoStart()
    {
        if (Current.AutoStart)
        {
            TryApplyAutoStart(true);
        }
    }

    /// <summary>成功時回傳 null；失敗時回傳要顯示在設定視窗的訊息。</summary>
    public string? Save(AppSettings next)
    {
        var previous = Current;
        try
        {
            _store.Save(next);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return SaveFailedMessage;
        }

        Current = next;
        Changed?.Invoke(next);
        // 只在開關改變時才動登錄，其他設定的變更不會改寫使用者的開機項目
        if (next.AutoStart != previous.AutoStart && !TryApplyAutoStart(next.AutoStart))
        {
            return AutoStartFailedMessage;
        }
        return null;
    }

    private bool TryApplyAutoStart(bool enabled)
    {
        try
        {
            _autoStart.Apply(enabled);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }
}
