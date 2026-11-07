using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Graphics;
using static LegionFanModeTool.Win32;

namespace LegionFanModeTool;

/// <summary>
/// 基于 Shell_NotifyIcon 的托盘图标。创建一个隐藏窗口接收托盘回调，
/// 消息由 WinUI 所在线程的消息循环分发。
/// </summary>
internal sealed unsafe partial class TrayIcon : IDisposable
{
    private const string WindowClass = "LegionFanModeTool.TrayIcon";
    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const uint IconId = 1;

    private readonly GCHandle _self;
    private readonly nint _hwnd;
    private readonly uint _taskbarCreatedMessage;
    private bool _added;

    /// <summary>左键单击或键盘选中。</summary>
    public event Action? Selected;

    /// <summary>请求显示菜单，参数为屏幕物理像素坐标。</summary>
    public event Action<PointInt32>? ContextMenuRequested;

    /// <summary>系统的亮色/暗色模式或强调色发生变化。</summary>
    public event Action? ThemeChanged;

    /// <summary>托盘图标。设置新图标后旧图标会被释放。</summary>
    public Icon? Icon
    {
        get;
        set
        {
            var old = field;
            field = value;
            Update(NifIcon);
            old?.Dispose();
        }
    }

    public string ToolTip
    {
        get;
        set
        {
            field = value;
            Update(NifTip | NifShowTip);
        }
    } = "";

    public TrayIcon()
    {
        _self = GCHandle.Alloc(this);
        _hwnd = CreateWindow(WindowClass, wndProc: &WndProc);
        SetWindowLongPtrW(_hwnd, GwlpUserData, GCHandle.ToIntPtr(_self));

        // 资源管理器重启后会广播 TaskbarCreated，主题变化会广播 WM_SETTINGCHANGE。
        // 本程序以管理员身份运行，需要允许它们穿过 UIPI。
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
        ChangeWindowMessageFilterEx(_hwnd, _taskbarCreatedMessage, MsgFltAllow, null);
        ChangeWindowMessageFilterEx(_hwnd, WmSettingChange, MsgFltAllow, null);
    }

    public void Show()
    {
        var data = CreateData(NifMessage | NifIcon | NifTip | NifShowTip);
        _added = Shell_NotifyIconW(NimAdd, &data);
        if (_added)
        {
            data.uTimeoutOrVersion = NotifyIconVersion4;
            Shell_NotifyIconW(NimSetVersion, &data);
        }
    }

    public void ShowError(string title, string text)
    {
        var data = CreateData(NifInfo);
        Copy(title, data.szInfoTitle, 64);
        Copy(text, data.szInfo, 256);
        data.dwInfoFlags = NiifError;
        Shell_NotifyIconW(NimModify, &data);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = CreateData(0);
            Shell_NotifyIconW(NimDelete, &data);
            _added = false;
        }
        DestroyWindow(_hwnd);
        _self.Free();
        Icon = null;
    }

    private void Update(uint flags)
    {
        if (!_added)
            return;
        var data = CreateData(flags);
        Shell_NotifyIconW(NimModify, &data);
    }

    private NOTIFYICONDATAW CreateData(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = Icon?.Handle ?? 0,
        };
        Copy(ToolTip, data.szTip, 128);
        return data;
    }

    private static void Copy(string text, char* buffer, int capacity)
    {
        var length = Math.Min(text.Length, capacity - 1);
        text.AsSpan(0, length).CopyTo(new Span<char>(buffer, capacity));
        buffer[length] = '\0';
    }

    private nint OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg == CallbackMessage)
        {
            // NOTIFYICON_VERSION_4：lParam 低位是事件，wParam 是锚点坐标
            switch ((uint)(lParam & 0xFFFF))
            {
                case NinSelect or NinKeySelect:
                    Selected?.Invoke();
                    break;
                case WmContextMenu:
                    ContextMenuRequested?.Invoke(new PointInt32((short)wParam, (short)(wParam >> 16)));
                    break;
            }
            return 0;
        }

        if (msg == WmSettingChange && lParam != 0
            && new string((char*)lParam) == "ImmersiveColorSet")
        {
            ThemeChanged?.Invoke();
            return 0;
        }

        if (msg == _taskbarCreatedMessage && msg != 0)
        {
            Show();
            return 0;
        }

        return DefWindowProcW(_hwnd, msg, wParam, lParam);
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam) =>
        GetWindowLongPtrW(hwnd, GwlpUserData) is not 0 and var self
            ? ((TrayIcon)GCHandle.FromIntPtr(self).Target!).OnMessage(msg, wParam, lParam)
            : DefWindowProcW(hwnd, msg, wParam, lParam);

    #region Win32

    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetVersion = 4;
    private const uint NifMessage = 0x01, NifIcon = 0x02, NifTip = 0x04, NifInfo = 0x10, NifShowTip = 0x80;
    private const uint NiifError = 0x03;
    private const uint NotifyIconVersion4 = 4;
    private const uint NinSelect = 0x400, NinKeySelect = 0x401;
    private const uint WmContextMenu = 0x007B;
    private const uint WmSettingChange = 0x001A;
    private const uint MsgFltAllow = 1;
    private const int GwlpUserData = -21;

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uTimeoutOrVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeWindowMessageFilterEx(nint hwnd, uint msg, uint action, void* changeFilterStruct);

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);

    #endregion
}
