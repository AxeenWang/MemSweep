using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using MemSweep.Core;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class TrayIconRendererTests
{
    private const uint UserObjects = 1;

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(nint process, uint flags);

    private static int CountPixels(Bitmap bitmap, Func<Color, bool> predicate)
    {
        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (predicate(bitmap.GetPixel(x, y)))
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static bool Near(Color pixel, Color target) =>
        pixel.A > 200
        && Math.Abs(pixel.R - target.R) < 40
        && Math.Abs(pixel.G - target.G) < 40
        && Math.Abs(pixel.B - target.B) < 40;

    [TestMethod]
    [DataRow(16)]
    [DataRow(24)]
    [DataRow(32)]
    public void RendersAnIconOfTheRequestedSize(int size)
    {
        using var icon = TrayIconRenderer.Render("80", UsageLevel.Orange, size);

        Assert.AreEqual(size, icon.Width);
        Assert.AreEqual(size, icon.Height);
    }

    [TestMethod]
    [DataRow("80", UsageLevel.Orange)]
    [DataRow("42", UsageLevel.Green)]
    [DataRow("100", UsageLevel.Red)]
    public void DrawsTheTextInTheLevelColorOnATransparentBackground(string text, UsageLevel level)
    {
        using var icon = TrayIconRenderer.Render(text, level, 32);
        using var bitmap = icon.ToBitmap();
        var color = TrayIconRenderer.ColorOf(level);

        Assert.IsGreaterThan(20, CountPixels(bitmap, p => Near(p, color)));
        foreach (var other in Enum.GetValues<UsageLevel>().Where(l => l != level))
        {
            Assert.AreEqual(0, CountPixels(bitmap, p => Near(p, TrayIconRenderer.ColorOf(other))));
        }
        Assert.AreEqual(0, bitmap.GetPixel(0, 0).A);
    }

    [TestMethod]
    public void LevelColorsAreDistinct()
    {
        var colors = Enum.GetValues<UsageLevel>().Select(TrayIconRenderer.ColorOf).Distinct().ToList();

        Assert.HasCount(3, colors);
    }

    [TestMethod]
    public void RenderingManyIconsDoesNotLeakHandles()
    {
        // 每 2 秒換一次圖示，漏掉 DestroyIcon 時 USER 物件會一直增加
        using var process = Process.GetCurrentProcess();
        long before = GetGuiResources(process.Handle, UserObjects);

        for (int i = 0; i < 300; i++)
        {
            using var icon = TrayIconRenderer.Render((i % 101).ToString(), UsageLevel.Red, 32);
        }

        long after = GetGuiResources(process.Handle, UserObjects);
        Assert.IsLessThan(30L, after - before);
    }
}
