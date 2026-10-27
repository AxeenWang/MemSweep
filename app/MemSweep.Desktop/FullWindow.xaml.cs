using System.Windows;
using System.Windows.Shell;

namespace MemSweep.Desktop;

/// <summary>
/// 完整視窗（F-20）：一般視窗，可調整大小。關閉時真正關閉並釋放，MemSweep 繼續常駐（A-08）；
/// 下次開啟由 <see cref="TrayController"/> 重新建立（Design 3.6），並還原上次的位置與大小。
/// </summary>
public partial class FullWindow : Window
{
    public FullWindow()
    {
        InitializeComponent();
        // 關閉之後 RestoreBounds 就是空的，要在關閉前記下
        Closing += (_, _) => LastPlacement = WindowPlacement.Capture(RestoreBounds, WindowState);
    }

    /// <summary>關閉前的位置與大小；沒顯示過就關閉時為 null。</summary>
    internal WindowPlacement? LastPlacement { get; private set; }

    /// <summary>
    /// 還原上次的位置、大小與最大化。沒有紀錄，或標題列已經不在 <paramref name="screen"/> 內時，
    /// 維持 XAML 的置中與預設大小。必須在第一次顯示之前呼叫。
    /// </summary>
    internal void Restore(WindowPlacement? placement, Rect screen)
    {
        if (placement is null || !placement.FitsOn(screen, WindowChrome.GetWindowChrome(this).CaptionHeight))
        {
            return;
        }
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placement.Bounds.Left;
        Top = placement.Bounds.Top;
        Width = placement.Bounds.Width;
        Height = placement.Bounds.Height;
        if (placement.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    public void ShowWindow()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Show();
        Activate();
    }
}
