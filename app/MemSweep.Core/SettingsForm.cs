using System.Globalization;
using System.Text;

namespace MemSweep.Core;

/// <summary>
/// 設定視窗的輸入內容（F-50、Design 3.6）：輸入框綁定文字，按「儲存」時才檢查；
/// 注音輸入法打出的全形數字也接受。
/// </summary>
public sealed class SettingsForm : ObservableObject
{
    public const string ThresholdError = "門檻必須是 1～100 的整數";
    public const string OrderError = "橘色門檻必須小於紅色門檻";
    public const string RetentionError = "保留天數必須是 1～3650 的整數";

    private string _orangeText;
    private string _redText;
    private bool _autoStart;
    private string _retentionText;
    private string? _error;

    public SettingsForm(AppSettings current)
    {
        _orangeText = Text(current.OrangeThreshold);
        _redText = Text(current.RedThreshold);
        _autoStart = current.AutoStart;
        _retentionText = Text(current.RetentionDays);
    }

    public string OrangeText { get => _orangeText; set => Set(ref _orangeText, value); }

    public string RedText { get => _redText; set => Set(ref _redText, value); }

    public bool AutoStart { get => _autoStart; set => Set(ref _autoStart, value); }

    public string RetentionText { get => _retentionText; set => Set(ref _retentionText, value); }

    /// <summary>顯示在按鈕上方的錯誤訊息；沒有錯誤時為 null。</summary>
    public string? Error { get => _error; private set => Set(ref _error, value); }

    /// <summary>檢查輸入：合法時清除錯誤訊息並回傳設定；不合法時顯示第一個錯誤並回傳 null。</summary>
    public AppSettings? TryRead()
    {
        if (!TryParse(OrangeText, 1, 100, out int orange) || !TryParse(RedText, 1, 100, out int red))
        {
            Error = ThresholdError;
            return null;
        }
        if (orange >= red)
        {
            Error = OrderError;
            return null;
        }
        if (!TryParse(RetentionText, AppSettings.MinRetentionDays, AppSettings.MaxRetentionDays, out int days))
        {
            Error = RetentionError;
            return null;
        }
        Error = null;
        return new AppSettings(orange, red, AutoStart, days);
    }

    /// <summary>儲存失敗時顯示原因，例如 data 資料夾無法寫入。</summary>
    public void ShowError(string message) => Error = message;

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    // 全形數字以相容分解轉成半形；不接受正負號、小數點與千分位
    private static bool TryParse(string text, int min, int max, out int value) =>
        int.TryParse(text.Normalize(NormalizationForm.FormKC).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && value >= min
        && value <= max;
}
