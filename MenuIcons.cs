using System.Drawing;
using System.IO;
using System.Text;
using System.Xml.Linq;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LegionFanModeTool;

/// <summary>为 WinUI 菜单生成矢量图标：着色后的模式图案 SVG，由系统按实际尺寸渲染。</summary>
internal static class MenuIcons
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    public static SvgImageSource Create(FanMode mode, Color color)
    {
        using var stream = mode.OpenSvg();
        var glyph = XDocument.Load(stream).Root!;

        // 菜单背景本身是深色，不需要托盘图标那样的底；裁到外圈（圆心 35，半径最大 32），让图案占满图标位
        var document = new XElement(Svg + "svg",
            new XAttribute("viewBox", "3 3 64 64"),
            new XElement(Svg + "g",
                new XAttribute("fill", $"#{color.R:X2}{color.G:X2}{color.B:X2}"),
                glyph.Elements()));

        var source = new SvgImageSource();
        var bytes = Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
        _ = source.SetSourceAsync(new MemoryStream(bytes).AsRandomAccessStream());
        return source;
    }
}
