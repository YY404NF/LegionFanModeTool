using System.Runtime.InteropServices;

namespace LegionFanModeTool;

/// <summary>多个类共用的 Win32 窗口函数。</summary>
internal static unsafe partial class Win32
{
    private const int ErrorClassAlreadyExists = 1410;

    /// <summary>
    /// 注册窗口类（同名类只注册一次）并创建窗口，窗口标题与类名相同。
    /// 不传 <paramref name="wndProc"/> 时使用默认窗口过程。
    /// </summary>
    public static nint CreateWindow(string className, uint style = 0, uint exStyle = 0,
        delegate* unmanaged<nint, uint, nint, nint, nint> wndProc = null)
    {
        var instance = GetModuleHandleW(null);
        fixed (char* name = className)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = wndProc is null ? &DefaultWindowProc : wndProc,
                hInstance = instance,
                lpszClassName = name,
            };
            if (RegisterClassExW(&wc) == 0 && Marshal.GetLastPInvokeError() != ErrorClassAlreadyExists)
                throw new InvalidOperationException($"RegisterClassEx 失败：{Marshal.GetLastPInvokeError()}");

            var hwnd = CreateWindowExW(exStyle, name, name, style, 0, 0, 0, 0, 0, 0, instance, null);
            return hwnd != 0 ? hwnd : throw new InvalidOperationException($"CreateWindowEx 失败：{Marshal.GetLastPInvokeError()}");
        }
    }

    [UnmanagedCallersOnly]
    private static nint DefaultWindowProc(nint hwnd, uint msg, nint wParam, nint lParam) =>
        DefWindowProcW(hwnd, msg, wParam, lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial nint GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    public static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(WNDCLASSEXW* wc);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint CreateWindowExW(uint exStyle, char* className, char* windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, void* param);
}
