using System.Windows;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

// 多個 STA 執行緒同時初始化 WPF 介面元件時會互相鎖死，介面測試一律依序執行
[TestClass]
[DoNotParallelize]
public sealed class WindowPlacementTests
{
    // 兩個螢幕組成的虛擬螢幕：主螢幕 1920×1080，左邊再一個 1280×1024
    private static readonly Rect Screen = new(-1280, 0, 3200, 1080);

    [TestMethod]
    public void NothingIsCapturedBeforeTheWindowWasShown()
    {
        Assert.IsNull(WindowPlacement.Capture(Rect.Empty, WindowState.Normal));
    }

    [TestMethod]
    [DataRow(WindowState.Normal, false)]
    [DataRow(WindowState.Maximized, true)]
    [DataRow(WindowState.Minimized, false)]
    public void CaptureKeepsTheNormalBoundsAndWhetherTheWindowWasMaximized(WindowState state, bool maximized)
    {
        var bounds = new Rect(300, 200, 900, 640);

        var placement = WindowPlacement.Capture(bounds, state);

        Assert.AreEqual(new WindowPlacement(bounds, maximized), placement);
    }

    [TestMethod]
    [DataRow(300, 200, true)]
    [DataRow(-1200, 100, true)]
    [DataRow(300, -10, false)]
    [DataRow(300, 1060, false)]
    [DataRow(1860, 200, false)]
    [DataRow(-2100, 200, false)]
    public void APlacementFitsOnlyWhenItsTitleBarCanStillBeGrabbed(double left, double top, bool fits)
    {
        // 標題列整列高度都在螢幕範圍內，而且至少露出 100 寬，才拖得回來
        var placement = new WindowPlacement(new Rect(left, top, 900, 640), false);

        Assert.AreEqual(fits, placement.FitsOn(Screen, 32));
    }

    [STATestMethod]
    public void WithoutAPlacementTheWindowOpensCenteredAtItsDefaultSize()
    {
        var window = new FullWindow();

        window.Restore(null, Screen);

        Assert.AreEqual(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
        Assert.AreEqual(760.0, window.Width);
        Assert.AreEqual(560.0, window.Height);
        Assert.AreEqual(WindowState.Normal, window.WindowState);
        window.Close();
    }

    [STATestMethod]
    public void TheWindowReopensWhereItWasClosed()
    {
        var window = new FullWindow();

        window.Restore(new WindowPlacement(new Rect(-1200, 100, 900, 640), false), Screen);

        Assert.AreEqual(WindowStartupLocation.Manual, window.WindowStartupLocation);
        Assert.AreEqual(new Rect(-1200, 100, 900, 640), new Rect(window.Left, window.Top, window.Width, window.Height));
        Assert.AreEqual(WindowState.Normal, window.WindowState);
        window.Close();
    }

    [STATestMethod]
    public void AMaximizedWindowReopensMaximizedAndKeepsItsNormalBounds()
    {
        var window = new FullWindow();

        window.Restore(new WindowPlacement(new Rect(300, 200, 900, 640), true), Screen);

        Assert.AreEqual(WindowState.Maximized, window.WindowState);
        Assert.AreEqual(new Rect(300, 200, 900, 640), new Rect(window.Left, window.Top, window.Width, window.Height));
        window.Close();
    }

    [STATestMethod]
    public void APlacementThatNoLongerFitsTheScreenFallsBackToTheCenter()
    {
        // 例如關掉視窗時在第二個螢幕，之後那個螢幕被拔掉
        var window = new FullWindow();

        window.Restore(new WindowPlacement(new Rect(-2100, 200, 900, 640), true), Screen);

        Assert.AreEqual(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
        Assert.AreEqual(760.0, window.Width);
        Assert.AreEqual(560.0, window.Height);
        Assert.AreEqual(WindowState.Normal, window.WindowState);
        window.Close();
    }
}
