using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace LegionFanModeTool;

/// <summary>
/// 模式菜单项文字后面的状态灯（小圆点），表示该模式是否参与左键循环切换。
/// MenuFlyoutItem 只能显示纯文本，这里在它的模板应用后，往布局网格里加一个圆点：
/// 垂直居中，水平位置随文字宽度放在文字后面。不改模板，悬停、按下等原生效果都保留。
/// </summary>
internal static class CycleIndicator
{
    private const double Size = 4.5;
    private const double Gap = 6;

    public static void Attach(MenuFlyoutItem item, Brush fill)
    {
        // Loaded 在每次打开菜单时都会触发，圆点只加一次
        Ellipse? dot = null;
        item.Loaded += (_, _) =>
        {
            if (dot is not null || FindTextBlock(item) is not { Parent: Grid layoutRoot } text)
                return;

            dot = new Ellipse
            {
                Width = Size,
                Height = Size,
                Fill = fill,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            layoutRoot.Children.Add(dot);

            // 模板里文字是水平拉伸的，ActualWidth 是整列宽度；DesiredSize 才是文字本身的宽度（已含左边距，
            // 左边距由图标占位状态决定）。布局后才有值，所以在尺寸变化时重新定位
            void Place() => dot.Margin = new Thickness(text.DesiredSize.Width + Gap, 0, 0, 0);
            text.SizeChanged += (_, _) => Place();
            Place();
        };
    }

    /// <summary>模板中显示文字的 TextBlock（WinUI 默认模板里名为 "TextBlock"）。</summary>
    internal static TextBlock? FindTextBlock(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock { Name: "TextBlock" } text)
                return text;
            if (FindTextBlock(child) is { } found)
                return found;
        }
        return null;
    }
}
