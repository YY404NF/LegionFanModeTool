using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LegionFanModeTool;

/// <summary>
/// 托盘程序：左键在安静/均衡间切换，右键菜单可选全部模式、开机自启和退出。
/// 模式变化通过订阅 BIOS 事件获得；风扇转速和温度只在鼠标悬停在图标上时读取。
/// </summary>
internal sealed class TrayApp : IDisposable
{
    /// <summary>订阅事件失败时退回的轮询间隔。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    /// <summary>悬停时读取传感器的最短间隔。</summary>
    private static readonly TimeSpan SensorInterval = TimeSpan.FromSeconds(1);
    private static readonly FanMode[] MenuModes = [FanMode.Balanced, FanMode.Performance, FanMode.Quiet];

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly TrayIcon _trayIcon = new();
    private readonly MenuHost _menuHost;
    private readonly ModeWatcher _modeWatcher = new();
    private readonly Dictionary<FanMode, MenuFlyoutItem> _modeItems = [];
    private readonly Dictionary<(FanMode Mode, bool Active), SvgImageSource> _menuIcons = [];
    private readonly MenuFlyoutItem _autoStartItem = new() { Text = "开机自启" };
    private readonly Stopwatch _sinceSensorRead = new();
    private DispatcherQueueTimer? _pollTimer;
    private FanMode _currentMode;
    private SensorReadings? _sensors;
    private bool _busy;
    private bool _refreshPending;
    private bool _sensorsBusy;

    public TrayApp()
    {
        foreach (var mode in MenuModes)
        {
            _menuIcons[(mode, true)] = MenuIcons.Create(mode, mode.AccentColor);
            _menuIcons[(mode, false)] = MenuIcons.Create(mode, FanModeExtensions.InactiveColor);
            _modeItems[mode] = CreateModeItem(mode);
        }

        _autoStartItem.Click += (_, _) => ToggleAutoStart();
        var exitItem = new MenuFlyoutItem { Text = "退出" };
        exitItem.Click += (_, _) => Exit();

        var menu = new MenuFlyout();
        foreach (var mode in MenuModes)
            menu.Items.Add(_modeItems[mode]);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exitItem);
        menu.Opening += (_, _) => SetChecked(_autoStartItem, AutoStart.IsEnabled());
        _menuHost = new MenuHost(menu);

        _trayIcon.Selected += () => _ = SwitchModeAsync(_currentMode == FanMode.Quiet ? FanMode.Balanced : FanMode.Quiet);
        _trayIcon.ContextMenuRequested += position => _ = _menuHost.ShowAsync(position);
        _trayIcon.Hovered += () => _ = RefreshSensorsAsync(force: false);
        _trayIcon.ToolTip = "散热模式：读取中…";
        _trayIcon.Icon = TrayIconRenderer.Create(_currentMode);
        _trayIcon.Show();

        StartWatching();
        _ = RefreshAsync(showErrors: true);
        _ = RefreshSensorsAsync(force: true);
    }

    private void StartWatching()
    {
        _modeWatcher.ModeChanged += () => _dispatcher.TryEnqueue(() => _ = RefreshAsync(showErrors: false));
        try
        {
            _modeWatcher.Start();
        }
        catch (Exception)
        {
            // 机型不发事件或订阅失败时退回定时轮询
            _pollTimer = _dispatcher.CreateTimer();
            _pollTimer.Interval = PollInterval;
            _pollTimer.Tick += (_, _) => _ = RefreshAsync(showErrors: false);
            _pollTimer.Start();
        }
    }

    private MenuFlyoutItem CreateModeItem(FanMode mode)
    {
        var item = new MenuFlyoutItem
        {
            Text = mode.DisplayName,
            Icon = new ImageIcon { Source = _menuIcons[(mode, false)] },
        };
        item.Click += (_, _) => _ = SwitchModeAsync(mode);
        return item;
    }

    private Task SwitchModeAsync(FanMode mode) =>
        RunAsync(() => LenovoGameZone.SetMode(mode), errorTitle: "切换失败");

    private Task RefreshAsync(bool showErrors) =>
        RunAsync(LenovoGameZone.GetMode, errorTitle: showErrors ? "读取失败" : null);

    /// <summary>
    /// 在后台线程执行 WMI 操作并更新界面；同一时间只执行一个。
    /// 执行期间收到的事件刷新会在结束后补做一次，避免漏掉 Fn+Q 等外部切换。
    /// </summary>
    private async Task RunAsync(Func<FanMode> operation, string? errorTitle)
    {
        if (_busy)
        {
            _refreshPending = true;
            return;
        }

        _busy = true;
        try
        {
            UpdateMode(await Task.Run(operation));
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
    }

    private async Task RefreshSensorsAsync(bool force)
    {
        if (_sensorsBusy || (!force && _sinceSensorRead.IsRunning && _sinceSensorRead.Elapsed < SensorInterval))
            return;

        _sensorsBusy = true;
        try
        {
            _sensors = await Task.Run(LenovoGameZone.GetSensors);
        }
        catch
        {
            _sensors = null; // 不支持的机型只显示模式
        }
        finally
        {
            _sensorsBusy = false;
            _sinceSensorRead.Restart();
        }
        UpdateToolTip();
    }


    private void UpdateMode(FanMode mode)
    {
        if (mode == _currentMode)
            return;

        _currentMode = mode;
        // 当前模式用彩色图标表示，其余为灰色
        foreach (var (itemMode, item) in _modeItems)
            ((ImageIcon)item.Icon).Source = _menuIcons[(itemMode, itemMode == mode)];
        _trayIcon.Icon = TrayIconRenderer.Create(mode);
        UpdateToolTip();
    }

    private void UpdateToolTip()
    {
        var text = $"散热模式：{_currentMode.DisplayName}";
        if (_sensors is { } s)
        {
            text += $"\n风扇：{s.Fan1Rpm} / {s.Fan2Rpm} RPM"
                  + $"\nCPU {s.CpuTemp}°C　GPU {s.GpuTemp}°C";
        }
        _trayIcon.ToolTip = text;
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
        item.Icon = isChecked ? new FontIcon { Glyph = "" } : null; // Segoe Fluent Icons：CheckMark

    private void Exit()
    {
        Dispose();
        Application.Current.Exit();
    }

    public void Dispose()
    {
        _pollTimer?.Stop();
        _modeWatcher.Dispose();
        _trayIcon.Dispose();
        _menuHost.Dispose();
    }
}
