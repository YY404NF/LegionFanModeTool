using System.Drawing;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace LegionFanModeTool;

/// <summary>
/// 系统主题与颜色。托盘图标跟随任务栏（Windows 模式），菜单跟随应用模式，两者可以不同；
/// 颜色取自 WinUI 主题资源和系统强调色，不写死。
/// </summary>
internal static class Theme
{
    private static readonly UISettings Settings = new();

    /// <summary>任务栏是否为亮色（设置 → 个性化 → 颜色 → 选择默认 Windows 模式）。</summary>
    public static bool IsTaskbarLight
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is 1;
        }
    }

    /// <summary>应用是否为亮色（设置 → 个性化 → 颜色 → 选择默认应用模式），菜单跟随它。</summary>
    public static bool IsAppLight => Settings.GetColorValue(UIColorType.Foreground) is { R: 0, G: 0, B: 0 };

    /// <summary>主要文字颜色，和原生图标、菜单文字一致（WinUI 资源 TextFillColorPrimary）。</summary>
    public static Color Foreground(bool light)
    {
        var resources = Application.Current.Resources.MergedDictionaries.OfType<XamlControlsResources>().Single();
        var themeDictionary = (ResourceDictionary)resources.ThemeDictionaries[light ? "Light" : "Default"];
        return ToDrawing((Windows.UI.Color)themeDictionary["TextFillColorPrimary"]);
    }

    /// <summary>
    /// 模式选中时的颜色：野兽、安静用联想电脑管家的品牌色；均衡原版是白色，
    /// 亮色主题下看不见，改用系统强调色（与 WinUI 强调色文字一样，亮色主题取加深一档）。
    /// </summary>
    public static Color ActiveColor(FanMode mode, bool light) => mode.BrandColor ?? (light
        ? ToDrawing(Settings.GetColorValue(UIColorType.AccentDark2))
        : Foreground(light: false));

    private static Color ToDrawing(Windows.UI.Color c) => Color.FromArgb(c.A, c.R, c.G, c.B);
}
