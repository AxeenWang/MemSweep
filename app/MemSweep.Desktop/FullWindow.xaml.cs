using System.Windows;

namespace MemSweep.Desktop;

/// <summary>
/// 完整視窗（F-20）：一般視窗，可調整大小。關閉時真正關閉並釋放，MemSweep 繼續常駐（A-08）；
/// 下次開啟由 <see cref="TrayController"/> 重新建立（Design 3.6）。
/// </summary>
public partial class FullWindow : Window
{
    public FullWindow()
    {
        InitializeComponent();
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
