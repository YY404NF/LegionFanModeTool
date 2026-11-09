using System.Drawing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LegionFanModeTool;

/// <summary>
/// 托盘程序：左键在菜单里亮绿灯的模式间循环切换，右键菜单可选全部模式、开机自启和退出。
/// 模式变化通过订阅 BIOS 事件获得。
/// </summary>
internal sealed class TrayApp : IDisposable
{
    private static readonly FanMode[] MenuModes = [FanMode.Quiet, FanMode.Balanced, FanMode.Performance, FanMode.Custom];


    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly TrayIcon _trayIcon = new();
    private readonly MenuHost _menuHost;
    private readonly ModeOsd _osd = new();
    private readonly ModeWatcher _modeWatcher = new();
    private readonly GlobalHotkey _altQ = new(HotkeyModifiers.Alt, 'Q');
    private readonly Dictionary<FanMode, MenuFlyoutItem> _modeItems = [];
    private readonly Dictionary<(SvgIcon Glyph, Color Color, double Scale), WriteableBitmap> _menuIcons = [];
    private MenuFlyoutItem? _modelItem;
    private readonly MenuFlyoutItem _autoStartItem = new() { Text = "开机自启" };
    private readonly MenuFlyoutItem _showOsdItem = new() { Text = "切换显示" };
    private readonly MenuFlyoutItem _altQItem = new() { Text = "Alt+Q" };
    private readonly Dictionary<FanMode, SolidColorBrush> _cycleDots = [];
    private readonly HashSet<FanMode> _cycleModes = [.. Settings.CycleModes ?? MenuModes];
    private FanMode _currentMode;
    private bool _busy;
    private bool _refreshPending;
    private bool _supportsCustomMode;

    public TrayApp()
    {
        foreach (var mode in MenuModes)
            _modeItems[mode] = CreateModeItem(mode);

        _autoStartItem.Click += (_, _) => ToggleAutoStart();
        _showOsdItem.Click += (_, _) => SetChecked(_showOsdItem, Settings.ShowOsd = !Settings.ShowOsd);
        _altQItem.Click += (_, _) => ToggleAltQ();
        var exitItem = new MenuFlyoutItem { Text = "退出", Icon = new FontIcon { Glyph = "\uE7E8" } }; // Segoe Fluent Icons：PowerButton
        exitItem.Click += (_, _) => Exit();

        var menu = new MenuFlyout();
        if (DeviceModel.DisplayName is { } model)
        {
            // 顶部型号行：只做展示（禁用、无悬停效果），没有映射的未知型号不显示
            _modelItem = DeviceHeader.Create(model);
            menu.Items.Add(_modelItem);
            menu.Items.Add(new MenuFlyoutSeparator());
        }
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

        _trayIcon.Selected += CycleMode;
        _trayIcon.ContextMenuRequested += position => _ = _menuHost.ShowAsync(position);
        _trayIcon.ThemeChanged += UpdateTrayIcon;
        _trayIcon.ToolTip = "散热模式：读取中…";
        UpdateTrayIcon();
        _trayIcon.Show();

        _altQ.Pressed += CycleMode;
        if (Settings.AltQHotkey)
            EnableAltQ();

        StartWatching();
        _ = InitializeModeAsync();
    }

    private async Task InitializeModeAsync()
    {
        SetCustomModeAvailable(await Task.Run(LenovoGameZone.SupportsCustomMode));
        await RefreshAsync(showErrors: true);
    }

    private void SetCustomModeAvailable(bool available)
    {
        _supportsCustomMode = available;
        _modeItems[FanMode.Custom].Visibility = available ? Visibility.Visible : Visibility.Collapsed;
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

    /// <summary>
    /// 模式菜单项。文字后面的状态灯（<see cref="CycleIndicator"/>）绿色表示参与左键循环切换，灰色表示不参与，
    /// 右键菜单项切换；切换时只改画刷颜色。
    /// </summary>
    private MenuFlyoutItem CreateModeItem(FanMode mode)
    {
        var item = new MenuFlyoutItem
        {
            Text = mode.DisplayName,
            Icon = new ImageIcon(),
            Visibility = mode == FanMode.Custom ? Visibility.Collapsed : Visibility.Visible,
        };
        var dot = new SolidColorBrush();
        CycleIndicator.Attach(item, dot);
        _cycleDots[mode] = dot;

        item.Click += (_, _) => _ = SwitchModeAsync(mode);
        item.RightTapped += (_, e) =>
        {
            e.Handled = true;
            ToggleCycleMode(mode);
        };
        return item;
    }

    /// <summary>左键单击托盘图标和 Alt+Q：按菜单顺序切换到下一个亮绿灯的模式；只有一个时切到它，一个都没有时不动作。</summary>
    private void CycleMode()
    {
        var start = Array.IndexOf(MenuModes, _currentMode); // 当前是极致等不在菜单中的模式时为 -1，从第一个开始找
        for (var i = 1; i <= MenuModes.Length; i++)
        {
            var mode = MenuModes[(start + i) % MenuModes.Length];
            if (_cycleModes.Contains(mode) && (mode != FanMode.Custom || _supportsCustomMode))
            {
                _ = SwitchModeAsync(mode);
                return;
            }
        }
    }

    private void ToggleCycleMode(FanMode mode)
    {
        if (!_cycleModes.Remove(mode))
            _cycleModes.Add(mode);
        Settings.CycleModes = [.. MenuModes.Where(_cycleModes.Contains)];
        UpdateCycleDots(Theme.IsAppLight);
    }

    /// <summary>状态灯颜色跟随应用主题（WinUI 资源 SystemFillColorSuccess / TextFillColorTertiary）。</summary>
    private void UpdateCycleDots(bool light)
    {
        foreach (var (mode, dot) in _cycleDots)
            dot.Color = (Windows.UI.Color)Theme.Resource(_cycleModes.Contains(mode) ? "SystemFillColorSuccess" : "TextFillColorTertiary", light);
    }

    /// <summary>切换模式，成功后像 Fn+Q 一样在屏幕上提示切换后的实际模式（可在菜单中关闭）。</summary>
    private async Task SwitchModeAsync(FanMode mode)
    {
        if (mode == FanMode.Custom && !_supportsCustomMode)
            return;

        if (await RunAsync($"切换到{mode.DisplayName}", () => LenovoGameZone.SetMode(mode), errorTitle: "切换失败") is not { } actual)
            return;

        // 不支持自定义模式的 BIOS 会忽略设置，模式保持不变
        if (mode == FanMode.Custom && actual != FanMode.Custom)
        {
            TestLog.Write($"自定义模式切换未生效，固件实际返回模式 {(int)actual}");
            SetCustomModeAvailable(false);
            _trayIcon.ShowError("切换失败", "当前机型或 BIOS 版本不支持自定义模式。");
        }
        else if (Settings.ShowOsd)
            _osd.Show(actual);
    }

    private Task<FanMode?> RefreshAsync(bool showErrors) =>
        RunAsync("读取当前模式", LenovoGameZone.GetMode, errorTitle: showErrors ? "读取失败" : null);

    /// <summary>
    /// 在后台线程执行 WMI 操作并更新界面，返回读到的模式（忙或失败时为 null）；同一时间只执行一个。
    /// 执行期间收到的事件刷新会在结束后补做一次，避免漏掉 Fn+Q 等外部切换。
    /// <paramref name="errorTitle"/> 为 null 表示后台刷新：失败时不提示，也不生成诊断信息。
    /// </summary>
    private async Task<FanMode?> RunAsync(string name, Func<FanMode> operation, string? errorTitle)
    {
        if (_busy)
        {
            _refreshPending = true;
            return null;
        }

        FanMode? result = null;
        Exception? error = null;
        _busy = true;
        TestLog.Write($"操作开始：{name}");
        try
        {
            result = await Task.Run(operation);
            TestLog.Write($"操作完成：{name}，实际模式：{result.Value.DisplayName}（{(int)result.Value}）");
            UpdateMode(result.Value);
        }
        catch (Exception ex)
        {
            // 后台刷新失败时保持安静，下次再试
            error = ex;
            TestLog.Write($"操作失败：{name}", ex);
            if (errorTitle is not null)
                _trayIcon.ShowError(errorTitle, ex.Message);
        }
        finally
        {
            _busy = false;
        }

        if (errorTitle is not null)
            Diagnostics.Report(name, result, error);

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
        _trayIcon.ToolTip = $"散热模式：{mode.DisplayName}{Diagnostics.Label}";
    }

    /// <summary>托盘图标和原生图标一样单色，颜色跟随任务栏主题。</summary>
    private void UpdateTrayIcon() =>
        _trayIcon.Icon = IconRenderer.CreateTrayIcon(_currentMode, Theme.Foreground(Theme.IsTaskbarLight));

    /// <summary>菜单图标跟随应用主题：未选中和型号旁的标志用文字颜色，当前模式用强调色。</summary>
    private void UpdateMenuIcons()
    {
        var light = Theme.IsAppLight;
        var scale = _menuHost.RasterizationScale;
        foreach (var (mode, item) in _modeItems)
        {
            var color = mode == _currentMode ? Theme.ActiveColor(mode, light) : Theme.Foreground(light);
            SetIcon(item, IconRenderer.Glyph(mode), color, scale);
        }
        if (_modelItem is not null)
            SetIcon(_modelItem, IconRenderer.Legion, Theme.Foreground(light), scale);
        UpdateCycleDots(light);
    }

    private void SetIcon(MenuFlyoutItem item, SvgIcon glyph, Color color, double scale)
    {
        if (!_menuIcons.TryGetValue((glyph, color, scale), out var source))
            _menuIcons[(glyph, color, scale)] = source = MenuIcons.Create(glyph, color, scale);
        ((ImageIcon)item.Icon).Source = source;
    }

    /// <summary>Alt+Q 与左键单击托盘图标相同，在亮绿灯的模式间循环切换。</summary>
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
        {
            TestLog.Write("注册 Alt+Q 全局快捷键失败");
            _trayIcon.ShowError("Alt+Q 不可用", "Alt+Q 已被其他程序注册为全局快捷键。");
        }
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
            TestLog.Write("设置开机自启失败", ex);
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
