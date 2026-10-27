using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MemSweep.Desktop;

/// <summary>小面板的內容（F-11、F-12）；DataContext 是 <see cref="Core.PanelModel"/>。</summary>
public partial class FlyoutPanel : UserControl
{
    private const string CopyLabel = "複製報告";

    private readonly DispatcherTimer _copyFeedback;

    public FlyoutPanel()
    {
        InitializeComponent();
        _copyFeedback = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _copyFeedback.Tick += (_, _) =>
        {
            _copyFeedback.Stop();
            CopyButton.Content = CopyLabel;
        };
    }

    public event Action? FullListClicked;

    public event Action? CopyClicked;

    public event Action? ExitClicked;

    /// <summary>按下「複製報告」後，按鈕文字短暫顯示結果。</summary>
    public void ShowCopyResult(bool succeeded)
    {
        CopyButton.Content = succeeded ? "已複製" : "複製失敗";
        _copyFeedback.Stop();
        _copyFeedback.Start();
    }

    private void FullListButton_Click(object sender, RoutedEventArgs e) => FullListClicked?.Invoke();

    private void CopyButton_Click(object sender, RoutedEventArgs e) => CopyClicked?.Invoke();

    private void ExitButton_Click(object sender, RoutedEventArgs e) => ExitClicked?.Invoke();
}
