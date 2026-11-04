using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;

namespace LegionFanModeTool;

/// <summary>
/// 承载托盘菜单的 WinUI 窗口：无边框、不出现在任务栏和 Alt+Tab，只有 1 像素大小，
/// 弹出时移到鼠标位置，菜单以独立窗口的形式显示在它外面。
/// </summary>
internal sealed partial class MenuHost : IDisposable
{
    private readonly Window _window;
    private readonly Grid _root = new();
    private readonly MenuFlyout _menu;
    private readonly nint _hwnd;

    public MenuHost(MenuFlyout menu)
    {
        _menu = menu;
        _menu.ShouldConstrainToRootBounds = false;

        _window = new Window { Content = _root };
        _menu.Closed += (_, _) => _window.AppWindow.Hide();
        _window.Activated += (_, e) =>
        {
            // 点击菜单外部时窗口失去激活，关闭菜单
            if (e.WindowActivationState == WindowActivationState.Deactivated)
                _menu.Hide();
        };

        var appWindow = _window.AppWindow;
        appWindow.SetPresenter(OverlappedPresenter.CreateForContextMenu());
        appWindow.IsShownInSwitchers = false;

        _hwnd = Win32Interop.GetWindowFromWindowId(appWindow.Id);
        SetWindowLongPtrW(_hwnd, GwlExStyle, GetWindowLongPtrW(_hwnd, GwlExStyle) | WsExToolWindow);
        // 去掉 Windows 11 给窗口加的圆角和边框，否则这个 1 像素窗口会显示成一个小圆圈
        DwmSetWindowAttribute(_hwnd, DwmwaWindowCornerPreference, DwmwcpDoNotRound, sizeof(uint));
        DwmSetWindowAttribute(_hwnd, DwmwaBorderColor, DwmwaColorNone, sizeof(uint));
    }

    /// <summary>菜单所在屏幕的缩放比例；窗口加载前用系统 DPI。</summary>
    public double RasterizationScale => _root.XamlRoot?.RasterizationScale ?? GetDpiForSystem() / 96.0;

    /// <summary>在屏幕物理像素坐标处显示菜单。</summary>
    public async Task ShowAsync(PointInt32 position)
    {
        _window.AppWindow.MoveAndResize(new RectInt32(position.X, position.Y, 1, 1));
        _window.Activate();
        // 托盘回调期间系统允许本进程抢占前台，这样点击菜单外部才能正常关闭菜单
        SetForegroundWindow(_hwnd);

        if (_root.XamlRoot is null)
        {
            var loaded = new TaskCompletionSource();
            _root.Loaded += (_, _) => loaded.TrySetResult();
            await loaded.Task;
        }

        _menu.ShowAt(_root, new FlyoutShowOptions
        {
            Position = new(0, 0),
            Placement = FlyoutPlacementMode.Auto,
            ShowMode = FlyoutShowMode.Standard,
        });
    }

    public void Dispose() => _window.Close();

    private const int GwlExStyle = -20;
    private const nint WsExToolWindow = 0x80;
    private const uint DwmwaWindowCornerPreference = 33;
    private const uint DwmwaBorderColor = 34;
    private const uint DwmwcpDoNotRound = 1;
    private const uint DwmwaColorNone = 0xFFFFFFFE;

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, in uint value, int size);
}
