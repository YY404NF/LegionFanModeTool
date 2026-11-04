using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LegionFanModeTool;

/// <summary>
/// 托盘程序：左键按 安静 → 均衡 → 野兽 循环切换，右键菜单可选全部模式、开机自启和退出。
/// 模式变化通过订阅 BIOS 事件获得。
/// </summary>
internal sealed class TrayApp : IDisposable
{
    private static readonly FanMode[] MenuModes = [FanMode.Balanced, FanMode.Performance, FanMode.Quiet];

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly TrayIcon _trayIcon = new();
    private readonly MenuHost _menuHost;
    private readonly ModeOsd _osd = new();
    private readonly ModeWatcher _modeWatcher = new();
    private readonly GlobalHotkey _altQ = new(HotkeyModifiers.Alt, 'Q');
    private readonly Dictionary<FanMode, MenuFlyoutItem> _modeItems = [];
    private readonly Dictionary<(FanMode Mode, Color Color, double Scale), WriteableBitmap> _menuIcons = [];
    private readonly MenuFlyoutItem _autoStartItem = new() { Text = "开机自启" };
    private readonly MenuFlyoutItem _showOsdItem = new() { Text = "切换显示" };
    private readonly MenuFlyoutItem _altQItem = new() { Text = "Alt+Q" };
    private FanMode _currentMode;
    private bool _busy;
    private bool _refreshPending;

    public TrayApp()
    {
        foreach (var mode in MenuModes)
            _modeItems[mode] = CreateModeItem(mode);

        _autoStartItem.Click += (_, _) => ToggleAutoStart();
        _showOsdItem.Click += (_, _) => SetChecked(_showOsdItem, Settings.ShowOsd = !Settings.ShowOsd);
        _altQItem.Click += (_, _) => ToggleAltQ();
        var exitItem = new MenuFlyoutItem { Text = "退出" };
        exitItem.Click += (_, _) => Exit();

        var menu = new MenuFlyout();
        foreach (var mode in MenuModes)
            menu.Items.Add(_modeItems[mode]);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(_showOsdItem);
        menu.Items.Add(_altQItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exitItem);
        menu.Opening += (_, _) =>
        {
            UpdateMenuIcons();
            SetChecked(_autoStartItem, AutoStart.IsEnabled());
            SetChecked(_showOsdItem, Settings.ShowOsd);
            SetChecked(_altQItem, _altQ.IsRegistered);
        };
        _menuHost = new MenuHost(menu);

        _trayIcon.Selected += () => _ = SwitchModeAsync(_currentMode.Next);
        _trayIcon.ContextMenuRequested += position => _ = _menuHost.ShowAsync(position);
        _trayIcon.ThemeChanged += UpdateTrayIcon;
        _trayIcon.ToolTip = "散热模式：读取中…";
        UpdateTrayIcon();
        _trayIcon.Show();

        _altQ.Pressed += () => _ = SwitchModeAsync(_currentMode.Next);
        if (Settings.AltQHotkey)
            EnableAltQ();

        StartWatching();
        _ = RefreshAsync(showErrors: true);
    }

    private void StartWatching()
    {
        _modeWatcher.ModeChanged += () => _dispatcher.TryEnqueue(() => _ = RefreshAsync(showErrors: false));
        try
        {
            _modeWatcher.Start();
        }
        catch (Exception ex)
        {
            // 订阅失败时，Fn+Q 等外部切换不会同步；本程序的切换仍会更新图标
            CrashLog.Write("订阅散热模式事件失败", ex);
        }
    }

    private MenuFlyoutItem CreateModeItem(FanMode mode)
    {
        var item = new MenuFlyoutItem
        {
            Text = mode.DisplayName,
            Icon = new ImageIcon(),
        };
        item.Click += (_, _) => _ = SwitchModeAsync(mode);
        return item;
    }

    /// <summary>切换模式，成功后像 Fn+Q 一样在屏幕上提示切换后的实际模式（可在菜单中关闭）。</summary>
    private async Task SwitchModeAsync(FanMode mode)
    {
        if (await RunAsync(() => LenovoGameZone.SetMode(mode), errorTitle: "切换失败") is { } actual && Settings.ShowOsd)
            _osd.Show(actual);
    }

    private Task<FanMode?> RefreshAsync(bool showErrors) =>
        RunAsync(LenovoGameZone.GetMode, errorTitle: showErrors ? "读取失败" : null);

    /// <summary>
    /// 在后台线程执行 WMI 操作并更新界面，返回读到的模式（忙或失败时为 null）；同一时间只执行一个。
    /// 执行期间收到的事件刷新会在结束后补做一次，避免漏掉 Fn+Q 等外部切换。
    /// </summary>
    private async Task<FanMode?> RunAsync(Func<FanMode> operation, string? errorTitle)
    {
        if (_busy)
        {
            _refreshPending = true;
            return null;
        }

        FanMode? result = null;
        _busy = true;
        try
        {
            result = await Task.Run(operation);
            UpdateMode(result.Value);
        }
        catch (Exception ex) when (errorTitle is not null)
        {
            _trayIcon.ShowError(errorTitle, ex.Message);
        }
        catch
        {
            // 后台刷新失败时保持安静，下次再试
        }
        finally
        {
            _busy = false;
        }

        if (_refreshPending)
        {
            _refreshPending = false;
            await RefreshAsync(showErrors: false);
        }
        return result;
    }

    private void UpdateMode(FanMode mode)
    {
        if (mode == _currentMode)
            return;

        _currentMode = mode;
        UpdateMenuIcons();
        UpdateTrayIcon();
        _trayIcon.ToolTip = $"散热模式：{mode.DisplayName}";
    }

    /// <summary>托盘图标和原生图标一样单色，颜色跟随任务栏主题。</summary>
    private void UpdateTrayIcon() =>
        _trayIcon.Icon = IconRenderer.CreateTrayIcon(_currentMode, Theme.Foreground(Theme.IsTaskbarLight));

    /// <summary>菜单图标跟随应用主题：未选中用文字颜色，当前模式用强调色。</summary>
    private void UpdateMenuIcons()
    {
        var light = Theme.IsAppLight;
        var scale = _menuHost.RasterizationScale;
        foreach (var (mode, item) in _modeItems)
        {
            var color = mode == _currentMode ? Theme.ActiveColor(mode, light) : Theme.Foreground(light);
            if (!_menuIcons.TryGetValue((mode, color, scale), out var source))
                _menuIcons[(mode, color, scale)] = source = MenuIcons.Create(mode, color, scale);
            ((ImageIcon)item.Icon).Source = source;
        }
    }

    /// <summary>Alt+Q 与左键单击相同：按 安静 → 均衡 → 野兽 循环切换。</summary>
    private void ToggleAltQ()
    {
        Settings.AltQHotkey = !_altQ.IsRegistered;
        if (Settings.AltQHotkey)
            EnableAltQ();
        else
            _altQ.Unregister();
        SetChecked(_altQItem, _altQ.IsRegistered);
    }

    private void EnableAltQ()
    {
        if (!_altQ.Register())
            _trayIcon.ShowError("Alt+Q 不可用", "Alt+Q 已被其他程序注册为全局快捷键。");
    }

    private void ToggleAutoStart()
    {
        try
        {
            if (AutoStart.IsEnabled())
                AutoStart.Disable();
            else
                AutoStart.Enable();
        }
        catch (Exception ex)
        {
            _trayIcon.ShowError("设置开机自启失败", ex.Message);
        }
        SetChecked(_autoStartItem, AutoStart.IsEnabled());
    }

    // 不用 ToggleMenuFlyoutItem：它会给整个菜单多加一列勾选框。勾号直接作为图标，和模式图标共用一列
    private static void SetChecked(MenuFlyoutItem item, bool isChecked) =>
        item.Icon = isChecked ? new FontIcon { Glyph = "\uE73E" } : null; // Segoe Fluent Icons：CheckMark

    private void Exit()
    {
        Dispose();
        Application.Current.Exit();
    }

    public void Dispose()
    {
        _modeWatcher.Dispose();
        _trayIcon.Dispose();
        _menuHost.Dispose();
        _osd.Dispose();
        _altQ.Dispose();
    }
}
