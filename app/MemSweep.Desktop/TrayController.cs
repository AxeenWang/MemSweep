using System.Drawing;
using System.Windows.Forms;
using MemSweep.Core;

namespace MemSweep.Desktop;

/// <summary>系統匣圖示、提示文字與右鍵選單（F-01～F-07）。必須在 UI 執行緒上使用。</summary>
internal sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private Icon? _icon;
    private string? _iconKey;

    public TrayController()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("結束 MemSweep", null, (_, _) => ExitRequested?.Invoke());
        _notifyIcon = new NotifyIcon { Text = "MemSweep", ContextMenuStrip = menu };
    }

    public event Action? ExitRequested;

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
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon?.Dispose();
    }
}
