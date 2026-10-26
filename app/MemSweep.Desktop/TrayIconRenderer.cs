using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MemSweep.Core;
using MemSweep.Desktop.Native;

namespace MemSweep.Desktop;

/// <summary>畫出系統匣的數字圖示（F-02、F-03、Design 3.5）。</summary>
internal static class TrayIconRenderer
{
    public static Color ColorOf(UsageLevel level) => level switch
    {
        UsageLevel.Green => Color.FromArgb(46, 160, 67),
        UsageLevel.Orange => Color.FromArgb(255, 159, 10),
        _ => Color.FromArgb(255, 69, 58),
    };

    /// <summary>回傳的圖示擁有自己的控制代碼，由呼叫端 Dispose。</summary>
    public static Icon Render(string text, UsageLevel level, int size)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(ColorOf(level)))
        using (var path = GlyphPath(text, size))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillPath(brush, path);
        }

        nint handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            // GetHicon 建立的控制代碼不會隨 Icon 釋放，必須自己銷毀
            User32.DestroyIcon(handle);
        }
    }

    // 依字形的實際外框縮放並置中，數字才能填滿小圖示；「100」較寬，會自動縮小
    private static GraphicsPath GlyphPath(string text, int size)
    {
        var path = new GraphicsPath();
        using var family = new FontFamily("Segoe UI");
        path.AddString(text, family, (int)FontStyle.Bold, 100f, PointF.Empty, StringFormat.GenericTypographic);
        var bounds = path.GetBounds();
        float margin = size >= 24 ? 2f : 1f;
        float scale = Math.Min((size - 2 * margin) / bounds.Width, (size - 2 * margin) / bounds.Height);
        using var transform = new Matrix();
        transform.Translate(size / 2f, size / 2f);
        transform.Scale(scale, scale);
        transform.Translate(-(bounds.X + bounds.Width / 2f), -(bounds.Y + bounds.Height / 2f));
        path.Transform(transform);
        return path;
    }
}
