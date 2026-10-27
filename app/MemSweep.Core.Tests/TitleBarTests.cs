using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shell;
using MemSweep.Core;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

// 多個 STA 執行緒同時初始化 WPF 介面元件時會互相鎖死，介面測試一律依序執行
[TestClass]
[DoNotParallelize]
public sealed class TitleBarTests
{
    private static readonly string MaximizeGlyph = ((char)0xE922).ToString();
    private static readonly string RestoreGlyph = ((char)0xE923).ToString();

    private static FullWindow NewWindow()
    {
        var model = new FullWindowModel(UsageThresholds.Default, TimeZoneInfo.Local);
        model.Update(ReportSamples.Typical(), new PeakState(null, PeakHistory.Empty, null));
        var window = new FullWindow { DataContext = model };
        WpfLayout.LaidOut((FrameworkElement)window.Content, 760);
        return window;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static Bitmap Pixels(Icon icon) => icon.ToBitmap();

    [STATestMethod]
    public void TheWindowReplacesTheSystemTitleBar()
    {
        var window = NewWindow();

        var chrome = WindowChrome.GetWindowChrome(window);
        Assert.IsNotNull(chrome);
        Assert.AreEqual(32.0, chrome.CaptionHeight);
        Assert.AreEqual(new Thickness(0), chrome.GlassFrameThickness);
        Assert.IsFalse(chrome.UseAeroCaptionButtons);
        Assert.AreEqual(32.0, window.Caption.ActualHeight);
        window.Close();
    }

    [STATestMethod]
    public void TitleBarShowsTheTitleAndThreeCaptionButtons()
    {
        var window = NewWindow();
        var caption = window.Caption;

        Assert.AreEqual("MemSweep", caption.TitleText.Text);
        var buttons = new[] { caption.MinimizeButton, caption.MaximizeButton, caption.CloseButton };
        CollectionAssert.AreEqual(new[] { "最小化", "最大化", "關閉" }, buttons.Select(b => b.ToolTip as string).ToArray());
        CollectionAssert.AreEqual(
            new[] { ((char)0xE921).ToString(), MaximizeGlyph, ((char)0xE8BB).ToString() },
            buttons.Select(b => b.Content as string).ToArray());
        Assert.IsTrue(buttons.All(WindowChrome.GetIsHitTestVisibleInChrome));
        window.Close();
    }

    [STATestMethod]
    public void CaptionButtonsMaximizeRestoreAndMinimizeTheWindow()
    {
        var window = NewWindow();
        var caption = window.Caption;

        Click(caption.MaximizeButton);
        Assert.AreEqual(WindowState.Maximized, window.WindowState);
        Assert.AreEqual(RestoreGlyph, caption.MaximizeButton.Content);
        Assert.AreEqual("還原", caption.MaximizeButton.ToolTip);

        Click(caption.MaximizeButton);
        Assert.AreEqual(WindowState.Normal, window.WindowState);
        Assert.AreEqual(MaximizeGlyph, caption.MaximizeButton.Content);

        Click(caption.MinimizeButton);
        Assert.AreEqual(WindowState.Minimized, window.WindowState);
        window.Close();
    }

    [STATestMethod]
    public void TheCloseButtonClosesTheWindow()
    {
        var window = NewWindow();
        bool closed = false;
        window.Closed += (_, _) => closed = true;

        Click(window.Caption.CloseButton);

        Assert.IsTrue(closed);
    }

    [STATestMethod]
    public void AMaximizedWindowInsetsItsContentByTheFrame()
    {
        var window = NewWindow();

        window.WindowState = WindowState.Maximized;
        Assert.AreEqual(TitleBar.MaximizedMargin, window.Frame.Margin);
        Assert.IsGreaterThan(0.0, window.Frame.Margin.Left);
        Assert.IsGreaterThan(0.0, window.Frame.Margin.Top);

        window.WindowState = WindowState.Normal;
        Assert.AreEqual(new Thickness(0), window.Frame.Margin);
        window.Close();
    }

    [TestMethod]
    public void TheMaximizedInsetIsTheResizeFramePlusThePaddedBorder()
    {
        // 原型實測 150%：框線 5 px 加邊框間距 6 px 才剛好；WPF 的 SystemParameters 只有框線的 3.33
        Assert.AreEqual(new Thickness(11 / 1.5), TitleBar.Inset(5, 5, 6, 144));
        Assert.AreEqual(new Thickness(8, 9, 8, 9), TitleBar.Inset(4, 5, 4, 96));
    }

    [STATestMethod]
    public void TheIconHasEveryStandardWindowsSize()
    {
        CollectionAssert.AreEquivalent(
            new[] { 16, 20, 24, 32, 40, 48, 64, 256 },
            AppIcon.Frames().Select(f => f.PixelWidth).ToArray());
        Assert.IsTrue(AppIcon.Frames().All(f => f.PixelWidth == f.PixelHeight));
    }

    [TestMethod]
    [DataRow(16, 16)]
    [DataRow(20, 20)]
    [DataRow(24, 24)]
    [DataRow(30, 32)]
    [DataRow(33, 40)]
    [DataRow(512, 256)]
    public void TheTitleBarPicksTheSmallestFrameThatIsLargeEnough(int pixels, int expected)
    {
        Assert.AreEqual(expected, AppIcon.PickSize([256, 16, 20, 24, 32, 40, 48, 64], pixels));
    }

    [STATestMethod]
    [DataRow("MemSweep.exe")]
    [DataRow("memsweep-cli.exe")]
    public void BothExecutablesCarryTheIcon(string exe)
    {
        // 測試輸出資料夾裡有兩支 exe；取出 exe 內嵌的圖示，與 Assets/MemSweep.ico 同尺寸的一格逐像素比對
        using var embedded = Icon.ExtractAssociatedIcon(Path.Combine(AppContext.BaseDirectory, exe))!;
        using var stream = System.Windows.Application.GetResourceStream(AppIcon.Source).Stream;
        using var expected = new Icon(stream, embedded.Width, embedded.Height);
        using var actualPixels = Pixels(embedded);
        using var expectedPixels = Pixels(expected);

        Assert.AreEqual(expected.Width, embedded.Width);
        for (int y = 0; y < actualPixels.Height; y++)
        {
            for (int x = 0; x < actualPixels.Width; x++)
            {
                Assert.AreEqual(expectedPixels.GetPixel(x, y), actualPixels.GetPixel(x, y), $"({x}, {y})");
            }
        }
    }
}
