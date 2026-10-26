using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Threading;
using MemSweep.Core;

namespace MemSweep.Desktop;

/// <summary>系統匣圖示、提示文字、右鍵選單、小面板與完整視窗（F-01～F-24）。必須在 UI 執行緒上使用。</summary>
internal sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private FlyoutWindow? _flyout;
    private FullWindow? _fullWindow;
    private FullWindowModel? _fullModel;
    private bool _flyoutRequested;
    private Icon? _icon;
    private string? _iconKey;
    private GroupedSnapshot? _latest;
    private PeakState? _latestPeaks;

    public TrayController()
    {
        // 「設定…」在 Phase 04 加入
        var menu = new ContextMenuStrip();
        menu.Items.Add("開啟小面板", null, (_, _) => ShowFlyout());
        menu.Items.Add("完整清單…", null, (_, _) => ShowFullWindow());
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
        // 還沒有第一次取樣的結果時沒有內容可顯示，先記下來，取樣完成後再顯示
        if (_latest is null)
        {
            _flyoutRequested = true;
            return;
        }
        var flyout = EnsureFlyout();
        flyout.PanelView.DataContext = BuildPanel();
        flyout.ShowPanel();
    }

    public void Update(GroupedSnapshot grouped, PeakState peaks)
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
        _latestPeaks = peaks;
        if (_flyoutRequested)
        {
            _flyoutRequested = false;
            ShowFlyout();
        }
        else if (_flyout is { IsVisible: true })
        {
            _flyout.PanelView.DataContext = BuildPanel();
        }
        if (_fullWindow is { IsVisible: true })
        {
            _fullModel!.Update(grouped, peaks);
        }
    }

    public void ShowFullWindow()
    {
        if (_latest is null)
        {
            return;
        }
        _flyout?.HidePanel();
        var window = EnsureFullWindow();
        _fullModel!.Update(_latest, _latestPeaks!);
        window.ShowWindow();
    }

    public void Dispose()
    {
        if (_fullWindow is { } window)
        {
            window.Closed -= OnFullWindowClosed;
            window.Close();
        }
        _flyout?.Close();
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon?.Dispose();
    }

    private PanelModel BuildPanel() =>
        PanelModel.Build(_latest!, _latestPeaks?.BootPeak, UsageThresholds.Default, _latestPeaks?.StorageWarning);

    // 第一次開啟時才建立，之後只顯示／隱藏：只用系統匣時，行程不必載入 WPF 視窗（約省 40 MB）
    private FlyoutWindow EnsureFlyout()
    {
        if (_flyout is null)
        {
            _flyout = new FlyoutWindow();
            _flyout.PanelView.FullListClicked += ShowFullWindow;
            _flyout.PanelView.CopyClicked += CopyReport;
            _flyout.PanelView.ExitClicked += () => ExitRequested?.Invoke();
        }
        return _flyout;
    }

    // 第一次開啟時才建立；展開狀態保存在 FullWindowModel，關閉後再開仍在
    private FullWindow EnsureFullWindow()
    {
        if (_fullWindow is null)
        {
            _fullModel ??= new FullWindowModel(UsageThresholds.Default, TimeZoneInfo.Local);
            _fullWindow = new FullWindow { DataContext = _fullModel };
            _fullWindow.Closed += OnFullWindowClosed;
        }
        return _fullWindow;
    }

    // 隱藏的視窗會保留整個畫面樹，實測讓 MemSweep 超過 100 MB（Design 3.6），所以關閉時真正釋放：
    // 等 WPF 處理完關閉的工作後，請 GC 回收並把空出來的記憶體還給系統。只回收 MemSweep 自己的記憶體。
    private void OnFullWindowClosed(object? sender, EventArgs e)
    {
        _fullWindow = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true)));
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
