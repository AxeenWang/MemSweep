namespace MemSweep.Desktop;

/// <summary>小面板放在工作區右下角，與邊緣保留固定間距（F-10、Design 3.6）。</summary>
internal static class FlyoutPlacement
{
    public const double Margin = 12;

    public static (double Left, double Top) Place(double areaRight, double areaBottom, double width, double height) =>
        (areaRight - width - Margin, areaBottom - height - Margin);
}
