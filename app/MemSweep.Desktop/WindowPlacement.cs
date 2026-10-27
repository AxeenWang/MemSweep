using System.Windows;

namespace MemSweep.Desktop;

/// <summary>
/// 完整視窗關閉時的一般狀態範圍與是否最大化，下次開啟時還原。
/// 只記在記憶體：完整視窗關閉時會真正釋放（Design 3.6），重新啟動 MemSweep 後回到螢幕中央。
/// </summary>
internal sealed record WindowPlacement(Rect Bounds, bool Maximized)
{
    /// <summary>標題列至少要有這麼寬留在螢幕內，才拖得回來。</summary>
    public const double MinVisibleWidth = 100;

    /// <summary>所有螢幕合起來的範圍（DIP）。</summary>
    public static Rect VirtualScreen => new(
        SystemParameters.VirtualScreenLeft,
        SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth,
        SystemParameters.VirtualScreenHeight);

    /// <summary>
    /// 關閉當下的狀態。<paramref name="restoreBounds"/> 是最大化或最小化之前的範圍；
    /// 視窗沒顯示過時是空的，回傳 null，下次照常置中。
    /// </summary>
    public static WindowPlacement? Capture(Rect restoreBounds, WindowState state) =>
        restoreBounds.IsEmpty ? null : new(restoreBounds, state == WindowState.Maximized);

    /// <summary>標題列整列高度都在螢幕範圍內，而且至少露出 <see cref="MinVisibleWidth"/> 寬。螢幕配置改過、位置已經看不到時為 false。</summary>
    public bool FitsOn(Rect screen, double captionHeight)
    {
        var caption = Rect.Intersect(new Rect(Bounds.X, Bounds.Y, Bounds.Width, captionHeight), screen);
        // 150% 縮放時座標不是整數，高度比較留一點誤差
        return !caption.IsEmpty && caption.Width >= MinVisibleWidth && caption.Height >= captionHeight - 0.5;
    }
}
