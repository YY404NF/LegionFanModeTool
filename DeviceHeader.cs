using System.Security;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace LegionFanModeTool;

/// <summary>
/// 菜单顶部的型号行：左侧 Legion 标志，型号用 Bahnschrift 半粗体，年份/代号显示成一块小铭牌。
/// MenuFlyoutItem 只能显示纯文本，这里换掉它的模板；布局（16×16 图标位、文字左边距 28、外边距 4,2）
/// 与 WinUI 默认菜单项一致，保证和下面的菜单项对齐。模板里没有悬停/按下/禁用状态，配合禁用后只做展示。
/// Bahnschrift 的行高上下不对称，按整行居中会偏上，所以文字用 TextLineBounds="Tight" 只按字形（大写字母高度到基线）排版：
/// 型号据此与图标垂直居中，铭牌比型号矮、放在型号右侧，顶边与型号的大写字母顶部对齐。
/// </summary>
internal static class DeviceHeader
{
    public static MenuFlyoutItem Create(DeviceName name)
    {
        var badge = name.Badge is null ? "" : $$"""
            <Border Background="{ThemeResource TextFillColorPrimaryBrush}" CornerRadius="2" Padding="2.5,1.5,2.5,1.5" VerticalAlignment="Top">
              <TextBlock Text="{{SecurityElement.Escape(name.Badge)}}" FontFamily="Bahnschrift" FontWeight="SemiBold" FontSize="8"
                         TextLineBounds="Tight" Foreground="{ThemeResource TextFillColorInverseBrush}" />
            </Border>
            """;

        var template = (ControlTemplate)XamlReader.Load($$"""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="MenuFlyoutItem">
              <Grid Padding="{TemplateBinding Padding}" Margin="4,2,4,2" Background="Transparent">
                <Viewbox Width="16" Height="16" HorizontalAlignment="Left" VerticalAlignment="Center">
                  <ContentPresenter Content="{TemplateBinding Icon}" />
                </Viewbox>
                <StackPanel Orientation="Horizontal" Spacing="3" Margin="28,0,0,0" VerticalAlignment="Center">
                  <TextBlock Text="{{SecurityElement.Escape(name.Model)}}" FontFamily="Bahnschrift" FontWeight="SemiBold" FontSize="15"
                             TextLineBounds="Tight" VerticalAlignment="Top" Foreground="{ThemeResource TextFillColorPrimaryBrush}" />
                  {{badge}}
                </StackPanel>
              </Grid>
            </ControlTemplate>
            """);

        return new MenuFlyoutItem
        {
            Text = name.Model + name.Badge, // 供讲述人等辅助功能读取
            IsEnabled = false,
            Icon = new ImageIcon(),
            Template = template,
        };
    }
}
