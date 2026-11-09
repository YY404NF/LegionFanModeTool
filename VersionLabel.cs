using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LegionFanModeTool;

/// <summary>
/// 菜单项右下角的小号淡色文字（"退出"一行显示版本号）。不用 KeyboardAcceleratorTextOverride：
/// 那会给整个菜单多加一列，所有菜单项都变宽。这里在模板应用后往布局网格里加一个靠右下对齐的文字，
/// 与菜单项文字共用一列，不改变菜单宽度。
/// </summary>
internal static class VersionLabel
{
    private const double FontSize = 11;

    public static void Attach(MenuFlyoutItem item, string text, Brush foreground)
    {
        // Loaded 在每次打开菜单时都会触发，文字只加一次
        TextBlock? label = null;
        item.Loaded += (_, _) =>
        {
            if (label is not null || CycleIndicator.FindTextBlock(item) is not { Parent: Grid layoutRoot })
                return;

            label = new TextBlock
            {
                Text = text,
                FontSize = FontSize,
                Foreground = foreground,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false,
            };
            Grid.SetColumnSpan(label, Math.Max(1, layoutRoot.ColumnDefinitions.Count));
            layoutRoot.Children.Add(label);
        };
    }
}
