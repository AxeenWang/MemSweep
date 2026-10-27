using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MemSweep.Desktop.Native;

namespace MemSweep.Desktop;

/// <summary>
/// 小面板視窗（F-10）：無邊框、不在工作列、最上層；失去焦點或按 Esc 就隱藏。
/// 只建立一次，之後以顯示／隱藏切換（Design 3.6）。
/// </summary>
public partial class FlyoutWindow : Window
{
    private readonly DispatcherTimer _inactiveCheck;
    private DateTime _hiddenAtUtc = DateTime.MinValue;
    private nint _foregroundAtShow;

    public FlyoutWindow()
    {
        InitializeComponent();
        _inactiveCheck = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _inactiveCheck.Tick += (_, _) => CheckWhileInactive();
        Deactivated += (_, _) => HidePanel();
        SizeChanged += (_, _) => PlaceAtCorner();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                HidePanel();
                e.Handled = true;
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
        if (!Activate())
        {
            // 沒有前景許可（例如由背景啟動的第二個執行個體叫出），改為定時檢查前景視窗
            _foregroundAtShow = User32.GetForegroundWindow();
            _inactiveCheck.Start();
        }
    }

    public void HidePanel()
    {
        _inactiveCheck.Stop();
        if (!IsVisible)
        {
            return;
        }
        Hide();
        _hiddenAtUtc = DateTime.UtcNow;
    }

    private void CheckWhileInactive()
    {
        switch (FlyoutPlacement.Next(IsActive, _foregroundAtShow, User32.GetForegroundWindow()))
        {
            case InactiveCheck.Hide:
                HidePanel();
                break;
            case InactiveCheck.StopWatching:
                _inactiveCheck.Stop();
                break;
        }
    }

    private void PlaceAtCorner()
    {
        // Windows 11 的通知區域固定在主螢幕底部的工作列，所以用主螢幕的工作區
        var area = SystemParameters.WorkArea;
        (Left, Top) = FlyoutPlacement.Place(area.Right, area.Bottom, ActualWidth, ActualHeight);
    }
}
