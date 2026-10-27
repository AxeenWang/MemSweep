using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MemSweep.Desktop.Native;

namespace MemSweep.Desktop;

/// <summary>
/// 自訂標題列（F-62、Design 3.6），放在使用 WindowChrome 的視窗頂端；按鈕直接操作所在的視窗。
/// </summary>
public partial class TitleBar : UserControl
{
    public TitleBar()
    {
        InitializeComponent();
        // 依目前的 DPI 挑最清楚的一格，例如 150% 時用 24 px
        Loaded += (_, _) => IconImage.Source = AppIcon.Frame((int)Math.Round(16 * VisualTreeHelper.GetDpi(this).DpiScaleX));
    }

    /// <summary>
    /// 使用 WindowChrome 的視窗最大化時，四邊會超出螢幕一個框線的寬度（調整大小的框線加上邊框間距），
    /// 內容要往內縮這麼多，標題列與按鈕才不會被切掉。WPF 的 SystemParameters 不含邊框間距，
    /// 所以直接向系統查詢，再依系統 DPI 換成 WPF 的單位（150% 時 5 + 6 px = 7.33）。
    /// </summary>
    public static Thickness MaximizedMargin =>
        Inset(
            User32.GetSystemMetrics(User32.SmCxSizeFrame),
            User32.GetSystemMetrics(User32.SmCySizeFrame),
            User32.GetSystemMetrics(User32.SmCxPaddedBorder),
            User32.GetDpiForSystem());

    /// <summary>框線與邊框間距是實際像素，依系統 DPI 換成 WPF 的單位。</summary>
    internal static Thickness Inset(int sizeFrameX, int sizeFrameY, int paddedBorder, uint dpi)
    {
        double scale = dpi / 96.0;
        double x = (sizeFrameX + paddedBorder) / scale;
        double y = (sizeFrameY + paddedBorder) / scale;
        return new Thickness(x, y, x, y);
    }

    private Window Host => Window.GetWindow(this)!;

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => Host.WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        Host.WindowState = Host.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Host.Close();
}
