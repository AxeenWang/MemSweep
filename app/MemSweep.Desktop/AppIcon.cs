using System.Windows.Media.Imaging;

namespace MemSweep.Desktop;

/// <summary>
/// 程式圖示 Assets/MemSweep.ico（F-62）：exe 的圖示由專案的 ApplicationIcon 設定，
/// 這裡讓自訂標題列取用適合尺寸的一格。
/// </summary>
internal static class AppIcon
{
    public static readonly Uri Source = new("pack://application:,,,/MemSweep;component/Assets/MemSweep.ico");

    /// <summary>挑選不小於 pixels 的最小一格；每一格都比較小時用最大的一格。</summary>
    public static int PickSize(IReadOnlyList<int> sizes, int pixels)
    {
        var ordered = sizes.Order().ToList();
        return ordered.FirstOrDefault(size => size >= pixels, ordered[^1]);
    }

    public static IReadOnlyList<BitmapFrame> Frames() =>
        BitmapDecoder.Create(Source, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames;

    public static BitmapFrame Frame(int pixels)
    {
        var frames = Frames();
        int size = PickSize(frames.Select(f => f.PixelWidth).ToList(), pixels);
        return frames.First(f => f.PixelWidth == size);
    }
}
