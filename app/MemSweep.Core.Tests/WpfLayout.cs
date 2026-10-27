using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MemSweep.Core.Tests;

/// <summary>在 STA 執行緒上不顯示視窗，直接排版 WPF 元件並走訪畫面樹。</summary>
internal static class WpfLayout
{
    public static T LaidOut<T>(T element, double width = double.PositiveInfinity)
        where T : FrameworkElement
    {
        element.Measure(new Size(width, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        return element;
    }

    /// <summary>像視窗一樣把元件排滿固定的大小；<see cref="LaidOut"/> 則會縮成內容需要的大小。</summary>
    public static T Sized<T>(T element, double width, double height)
        where T : FrameworkElement
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        return element;
    }

    public static T Ancestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (var node = VisualTreeHelper.GetParent(element); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is T found)
            {
                return found;
            }
        }
        throw new InvalidOperationException($"找不到上層的 {typeof(T).Name}");
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    public static List<string> Texts(DependencyObject root) =>
        Descendants(root).OfType<TextBlock>().Select(t => t.Text).ToList();
}
