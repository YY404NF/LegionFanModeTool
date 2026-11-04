using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LegionFanModeTool;

/// <summary>
/// 绘制托盘图标：深灰圆角底（让图案在任意颜色的任务栏上都看得清）+ 模式图案，
/// 按当前 DPI 下的托盘图标像素尺寸直接绘制矢量。
/// </summary>
internal static partial class TrayIconRenderer
{
    // 取自联想电脑管家散热模式页面的卡片颜色
    private static readonly Color TileColor = Color.FromArgb(0x2A, 0x2A, 0x2A);
    private const float CornerRatio = 0.22f;
    private const float GlyphRatio = 0.92f;

    private static readonly Dictionary<FanMode, SvgIcon> Glyphs = [];

    /// <summary>创建当前模式的托盘图标，调用方负责释放。</summary>
    public static Icon Create(FanMode mode)
    {
        var size = GetSystemMetrics(SmCxSmIcon);
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            using (var tile = RoundedRect(new RectangleF(0, 0, size, size), size * CornerRatio))
            using (var brush = new SolidBrush(TileColor))
                g.FillPath(brush, tile);

            var glyphSize = size * GlyphRatio;
            var offset = (size - glyphSize) / 2;
            GetGlyph(mode).Draw(g, new RectangleF(offset, offset, glyphSize, glyphSize), mode.AccentColor);
        }

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

    private static SvgIcon GetGlyph(FanMode mode)
    {
        if (!Glyphs.TryGetValue(mode, out var glyph))
        {
            using var stream = mode.OpenSvg();
            Glyphs[mode] = glyph = new SvgIcon(stream);
        }
        return glyph;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private const int SmCxSmIcon = 49;

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint hIcon);
}
