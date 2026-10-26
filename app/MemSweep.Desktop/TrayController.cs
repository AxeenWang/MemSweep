using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MemSweep.Core;

namespace MemSweep.Desktop;

/// <summary>系統匣圖示、提示文字、右鍵選單與小面板（F-01～F-12）。必須在 UI 執行緒上使用。</summary>
internal sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private FlyoutWindow? _flyout;
    private Icon? _icon;
    private string? _iconKey;
    private GroupedSnapshot? _latest;
    private BootPeak? _latestPeak;

    public TrayController()
    {
        // 「完整清單…」在 Phase 03、「設定…」在 Phase 04 加入
        var menu = new ContextMenuStrip();
        menu.Items.Add("開啟小面板", null, (_, _) => ShowFlyout());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("結束 MemSweep", null, (_, _) => ExitRequested?.Invoke());
        _notifyIcon = new NotifyIcon { Text = "MemSweep", ContextMenuStrip = menu };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ToggleFlyout();
            }
        };
    }

    public event Action? ExitRequested;

    public void ShowFlyout()
    {
        // 還沒有第一次取樣的結果時沒有內容可顯示
        if (_latest is null)
        {
            return;
        }
        var flyout = EnsureFlyout();
        flyout.PanelView.DataContext = PanelModel.Build(_latest, _latestPeak, UsageThresholds.Default);
        flyout.ShowPanel();
    }

    public void Update(GroupedSnapshot grouped, BootPeak? peak)
    {
        var snapshot = grouped.Snapshot;
        string text = TrayText.IconText(snapshot);
        var level = UsageThresholds.Default.LevelOf(TrayText.CommitPercent(snapshot));
        int size = SystemInformation.SmallIconSize.Width;

        // 數字、顏色與尺寸都沒變就不重畫
        string key = $"{text}|{level}|{size}";
        if (key != _iconKey)
        {
            var previous = _icon;
            _icon = TrayIconRenderer.Render(text, level, size);
            _iconKey = key;
            _notifyIcon.Icon = _icon;
            previous?.Dispose();
        }

        _notifyIcon.Text = TrayText.Tooltip(snapshot);
        _notifyIcon.Visible = true;
        _latest = grouped;
        _latestPeak = peak;
        if (_flyout is { IsVisible: true })
        {
            _flyout.PanelView.DataContext = PanelModel.Build(grouped, peak, UsageThresholds.Default);
        }
    }

    public void Dispose()
    {
        _flyout?.Close();
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon?.Dispose();
    }

    // 第一次開啟時才建立，之後只顯示／隱藏：只用系統匣時，行程不必載入 WPF 視窗（約省 40 MB）
    private FlyoutWindow EnsureFlyout()
    {
        if (_flyout is null)
        {
            _flyout = new FlyoutWindow();
            _flyout.PanelView.CopyClicked += CopyReport;
            _flyout.PanelView.ExitClicked += () => ExitRequested?.Invoke();
        }
        return _flyout;
    }

    private void ToggleFlyout()
    {
        if (_flyout is { IsVisible: true })
        {
            _flyout.HidePanel();
        }
        else if (_flyout is null || !_flyout.WasJustHidden)
        {
            ShowFlyout();
        }
    }

    private void CopyReport()
    {
        if (_latest is null || _flyout is null)
        {
            return;
        }

        bool succeeded;
        try
        {
            System.Windows.Clipboard.SetText(ReportFormatter.ToText(_latest));
            succeeded = true;
        }
        catch (ExternalException)
        {
            // 剪貼簿被其他程式佔用
            succeeded = false;
        }
        _flyout.PanelView.ShowCopyResult(succeeded);
    }
}
