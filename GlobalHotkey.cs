using System.Runtime.InteropServices;
using static LegionFanModeTool.Win32;

namespace LegionFanModeTool;

[Flags]
internal enum HotkeyModifiers : uint
{
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>
/// 系统全局热键（RegisterHotKey）。注册后这个组合键由本程序独占，其他程序收不到。
/// 创建一个隐藏窗口接收 WM_HOTKEY，由 WinUI 所在线程的消息循环分发。
/// </summary>
internal sealed unsafe partial class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 1;
    private const uint ModNoRepeat = 0x4000;
    private const uint WmHotkey = 0x0312;
    private const int GwlpUserData = -21;

    private readonly GCHandle _self;
    private readonly nint _hwnd;
    private readonly uint _modifiers;
    private readonly uint _key;

    /// <param name="key">字母或数字键，例如 'Q'。</param>
    public GlobalHotkey(HotkeyModifiers modifiers, char key)
    {
        _modifiers = (uint)modifiers | ModNoRepeat;
        _key = char.ToUpperInvariant(key);
        _self = GCHandle.Alloc(this);
        _hwnd = CreateWindow("LegionFanModeTool.GlobalHotkey", wndProc: &WndProc);
        SetWindowLongPtrW(_hwnd, GwlpUserData, GCHandle.ToIntPtr(_self));
    }

    public event Action? Pressed;

    public bool IsRegistered { get; private set; }

    /// <summary>注册热键；已被其他程序占用时返回 false。</summary>
    public bool Register() => IsRegistered = IsRegistered || RegisterHotKey(_hwnd, HotkeyId, _modifiers, _key);

    public void Unregister()
    {
        if (IsRegistered)
            UnregisterHotKey(_hwnd, HotkeyId);
        IsRegistered = false;
    }

    public void Dispose()
    {
        Unregister();
        DestroyWindow(_hwnd);
        _self.Free();
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmHotkey && GetWindowLongPtrW(hwnd, GwlpUserData) is not 0 and var self)
        {
            ((GlobalHotkey)GCHandle.FromIntPtr(self).Target!).Pressed?.Invoke();
            return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hwnd, int id);
}
