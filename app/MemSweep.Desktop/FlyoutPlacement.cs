namespace MemSweep.Desktop;

/// <summary>小面板沒有取得焦點時，下一次檢查要做的事。</summary>
internal enum InactiveCheck
{
    KeepWatching,
    Hide,
    StopWatching,
}

/// <summary>小面板放在工作區右下角，與邊緣保留固定間距（F-10、Design 3.6）。</summary>
internal static class FlyoutPlacement
{
    public const double Margin = 12;

    public static (double Left, double Top) Place(double areaRight, double areaBottom, double width, double height) =>
        (areaRight - width - Margin, areaBottom - height - Margin);

    /// <summary>
    /// 小面板開啟時沒拿到焦點（沒有前景許可），就收不到失去焦點的事件：
    /// 使用者點了其他視窗、前景視窗換掉時收起；小面板之後拿到焦點，就交回一般的失去焦點處理。
    /// </summary>
    public static InactiveCheck Next(bool isActive, nint foregroundAtShow, nint foregroundNow) =>
        isActive ? InactiveCheck.StopWatching
        : foregroundNow != foregroundAtShow ? InactiveCheck.Hide
        : InactiveCheck.KeepWatching;
}
