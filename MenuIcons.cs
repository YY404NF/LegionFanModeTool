using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LegionFanModeTool;

/// <summary>
/// 菜单图标：按显示缩放算出的实际像素尺寸绘制成位图，以 1:1 像素显示。
/// 不用 SvgImageSource：它在 16px 这样的小尺寸下抗锯齿效果很差，而且从内存流加载时，
/// 流被 GC 回收后 XAML 再次解码会触发 E_POINTER 崩溃。
/// </summary>
internal static class MenuIcons
{
    /// <summary>菜单图标位的逻辑尺寸（DIP）。</summary>
    private const double Size = 16;

    public static WriteableBitmap Create(FanMode mode, Color color, double scale)
    {
        var pixels = (int)Math.Round(Size * scale);
        using var bitmap = IconRenderer.Render(mode, pixels, color);

        // WriteableBitmap 需要预乘 alpha 的 BGRA
        var data = bitmap.LockBits(new Rectangle(0, 0, pixels, pixels), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        var buffer = new byte[data.Stride * pixels];
        try
        {
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        // 图标位是 16 DIP 的 Viewbox：位图按像素数当作 DIP 尺寸，被缩回 16 DIP 后正好是 1:1 物理像素
        var source = new WriteableBitmap(pixels, pixels);
        using (var stream = source.PixelBuffer.AsStream())
            stream.Write(buffer);
        source.Invalidate();
        return source;
    }
}
