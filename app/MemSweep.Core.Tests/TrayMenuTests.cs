using System.Windows.Forms;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class TrayMenuTests
{
    [TestMethod]
    public void TheMenuListsTheSpecItemsInOrder()
    {
        using var menu = TrayMenu.Build(() => { }, () => { }, () => { }, () => { });

        CollectionAssert.AreEqual(
            new[] { "開啟小面板", "完整清單…", "設定…", string.Empty, "結束 MemSweep" },
            menu.Items.Cast<ToolStripItem>().Select(i => i.Text).ToArray());
        Assert.IsInstanceOfType<ToolStripSeparator>(menu.Items[3]);
    }

    [TestMethod]
    public void EachItemRunsItsAction()
    {
        var clicked = new List<string>();
        using var menu = TrayMenu.Build(
            () => clicked.Add("flyout"),
            () => clicked.Add("full"),
            () => clicked.Add("settings"),
            () => clicked.Add("exit"));

        foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
        {
            item.PerformClick();
        }

        CollectionAssert.AreEqual(new[] { "flyout", "full", "settings", "exit" }, clicked);
    }
}
