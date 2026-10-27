using System.Windows.Forms;

namespace MemSweep.Desktop;

/// <summary>系統匣的右鍵選單（F-07）。</summary>
internal static class TrayMenu
{
    public static ContextMenuStrip Build(Action showFlyout, Action showFullWindow, Action showSettings, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("開啟小面板", null, (_, _) => showFlyout());
        menu.Items.Add("完整清單…", null, (_, _) => showFullWindow());
        menu.Items.Add("設定…", null, (_, _) => showSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("結束 MemSweep", null, (_, _) => exit());
        return menu;
    }
}
