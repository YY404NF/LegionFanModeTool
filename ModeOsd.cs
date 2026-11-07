using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using static LegionFanModeTool.Win32;

namespace LegionFanModeTool;

/// <summary>
/// 切换模式时的屏幕提示，复刻联想 Fn 热键程序（FnHotkeyUtility）按 Fn+Q 时的效果：
/// 主显示器水平居中、中心位于屏幕高度 3/4 处，图片原样放大 2 倍（150% 缩放下实测）并按逐像素透明度显示，
/// 显示 2 秒后直接消失。窗口置顶、不抢焦点、鼠标穿透。再次显示时更换图片并重新计时。
/// </summary>
internal sealed unsafe partial class ModeOsd : IDisposable
{
    private const int ImageSize = 160;
    private static readonly TimeSpan HoldTime = TimeSpan.FromSeconds(2);

    private readonly nint _hwnd;
    private readonly DispatcherQueueTimer _timer;

    public ModeOsd()
    {
        _hwnd = CreateWindow("LegionFanModeTool.ModeOsd", WsPopup,
            WsExLayered | WsExTopmost | WsExToolWindow | WsExNoActivate | WsExTransparent);

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = HoldTime;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => ShowWindow(_hwnd, SwHide);
    }

    public void Show(FanMode mode)
    {
        // 主显示器（与联想程序一致），按它的 DPI 决定尺寸
        var monitor = MonitorFromPoint(default, MonitorDefaultToPrimary);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        GetMonitorInfoW(monitor, &info);
        GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpi, out _);

        var size = (int)Math.Round(ImageSize * dpi / 72.0);
        var screen = info.rcMonitor;
        var position = new POINT(
            screen.Left + (screen.Right - screen.Left - size) / 2,
            screen.Top + (screen.Bottom - screen.Top) * 3 / 4 - size / 2);

        using (var stream = mode.OpenOsdImage())
        using (var image = new Bitmap(stream))
            Present(image, position, size);

        ShowWindow(_hwnd, SwShowNoActivate);
        _timer.Stop(); // 重新计时
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        DestroyWindow(_hwnd);
    }

    /// <summary>把图片缩放到 <paramref name="size"/> 并作为分层窗口的内容（预乘 alpha 的 32 位 DIB）。</summary>
    private void Present(Bitmap image, POINT position, int size)
    {
        using var scaled = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(image, 0, 0, size, size);
        }

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = size,
            biHeight = -size, // 自上而下
            biPlanes = 1,
            biBitCount = 32,
        };
        var dib = CreateDIBSection(0, &header, DibRgbColors, out var bits, 0, 0);
        var dc = CreateCompatibleDC(0);
        var previous = SelectObject(dc, dib);
        try
        {
            var data = scaled.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                Buffer.MemoryCopy((void*)data.Scan0, (void*)bits, (long)size * size * 4, (long)data.Stride * size);
            }
            finally
            {
                scaled.UnlockBits(data);
            }

            var sizeStruct = new SIZE(size, size);
            var origin = default(POINT);
            var blend = new BLENDFUNCTION { BlendOp = AcSrcOver, SourceConstantAlpha = 255, AlphaFormat = AcSrcAlpha };
            UpdateLayeredWindow(_hwnd, 0, &position, &sizeStruct, dc, &origin, 0, &blend, UlwAlpha);
        }
        finally
        {
            SelectObject(dc, previous);
            DeleteObject(dib);
            DeleteDC(dc);
        }
    }

    #region Win32

    private const uint WsPopup = 0x80000000;
    private const uint WsExLayered = 0x80000, WsExTopmost = 0x8, WsExToolWindow = 0x80, WsExNoActivate = 0x8000000, WsExTransparent = 0x20;
    private const int SwHide = 0, SwShowNoActivate = 4;
    private const uint UlwAlpha = 2;
    private const byte AcSrcOver = 0, AcSrcAlpha = 1;
    private const uint DibRgbColors = 0;
    private const uint MonitorDefaultToPrimary = 1;
    private const int MdtEffectiveDpi = 0;

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct POINT(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct SIZE(int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateLayeredWindow(nint hwnd, nint hdcDst, POINT* pptDst, SIZE* psize,
        nint hdcSrc, POINT* pptSrc, uint crKey, BLENDFUNCTION* pblend, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(POINT point, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint monitor, MONITORINFO* info);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint hdc, BITMAPINFOHEADER* bmi, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint hdc);

    #endregion
}
