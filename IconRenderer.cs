using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LegionFanModeTool;

/// <summary>
/// 按目标像素尺寸绘制单色图标（Assets 中的 SVG）。
/// 托盘和菜单共用，保证两处抗锯齿效果一致。
/// </summary>
internal static partial class IconRenderer
{
    private static readonly Dictionary<FanMode, SvgIcon> Glyphs = [];

    /// <summary>Legion 标志（"LEGION" 中的 O），显示在菜单顶部的型号旁。</summary>
    public static SvgIcon Legion { get; } = Load("legion.svg");

    /// <summary>绘制 <paramref name="size"/>×<paramref name="size"/> 像素的图标，调用方负责释放。</summary>
    public static Bitmap Render(SvgIcon glyph, int size, Color color)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        glyph.Draw(g, new RectangleF(0, 0, size, size), color);
        return bitmap;
    }

    /// <summary>按当前 DPI 下的托盘图标尺寸创建图标，调用方负责释放。</summary>
    public static Icon CreateTrayIcon(FanMode mode, Color color)
    {
        using var bitmap = Render(Glyph(mode), GetSystemMetrics(SmCxSmIcon), color);
        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public static SvgIcon Glyph(FanMode mode)
    {
        if (!Glyphs.TryGetValue(mode, out var glyph))
        {
            using var stream = mode.OpenSvg();
            Glyphs[mode] = glyph = new SvgIcon(stream);
        }
        return glyph;
    }

    private static SvgIcon Load(string name)
    {
        using var stream = EmbeddedAssets.Open(name);
        return new SvgIcon(stream);
    }

    private const int SmCxSmIcon = 49;

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint hIcon);
}
