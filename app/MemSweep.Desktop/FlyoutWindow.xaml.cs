using System.Windows;
using System.Windows.Input;

namespace MemSweep.Desktop;

/// <summary>
/// 小面板視窗（F-10）：無邊框、不在工作列、最上層；失去焦點或按 Esc 就隱藏。
/// 只建立一次，之後以顯示／隱藏切換（Design 3.6）。
/// </summary>
public partial class FlyoutWindow : Window
{
    private DateTime _hiddenAtUtc = DateTime.MinValue;

    public FlyoutWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => HidePanel();
        SizeChanged += (_, _) => PlaceAtCorner();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                HidePanel();
            }
        };
    }

    /// <summary>
    /// 剛因失去焦點而隱藏。點系統匣圖示時焦點會先移走、讓小面板隱藏，同一次點擊不應再把它打開。
    /// </summary>
    public bool WasJustHidden => DateTime.UtcNow - _hiddenAtUtc < TimeSpan.FromMilliseconds(400);

    public void ShowPanel()
    {
        Show();
        PlaceAtCorner();
        Activate();
    }

    public void HidePanel()
    {
        if (!IsVisible)
        {
            return;
        }
        Hide();
        _hiddenAtUtc = DateTime.UtcNow;
    }

    private void PlaceAtCorner()
    {
        // Windows 11 的通知區域固定在主螢幕底部的工作列，所以用主螢幕的工作區
        var area = SystemParameters.WorkArea;
        (Left, Top) = FlyoutPlacement.Place(area.Right, area.Bottom, ActualWidth, ActualHeight);
    }
}
