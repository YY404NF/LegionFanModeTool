using System;
using System.Collections.Generic;
using System.Management;

namespace LegionFanModeTool;

/// <summary>
/// 订阅 BIOS 的散热模式变化事件，代替定时轮询。Fn+Q、联想电脑管家或本程序切换模式时都会触发。
/// 不同机型发出的事件不同（R9000P 2021 只发 THERMAL_MODE_EVENT），所以两个都订阅。
/// 事件在线程池线程上触发。
/// </summary>
internal sealed class ModeWatcher : IDisposable
{
    private static readonly string[] EventClasses =
    [
        "LENOVO_GAMEZONE_THERMAL_MODE_EVENT",
        "LENOVO_GAMEZONE_SMART_FAN_MODE_EVENT",
    ];

    private readonly List<ManagementEventWatcher> _watchers = [];

    public event Action? ModeChanged;

    /// <summary>开始订阅。全部订阅失败时抛出异常，调用方可以退回轮询。</summary>
    public void Start()
    {
        var scope = LenovoGameZone.ConnectScope();
        Exception? lastError = null;
        foreach (var eventClass in EventClasses)
        {
            var watcher = new ManagementEventWatcher(scope, new EventQuery($"SELECT * FROM {eventClass}"));
            watcher.EventArrived += OnEventArrived;
            try
            {
                watcher.Start();
                _watchers.Add(watcher);
            }
            catch (ManagementException ex)
            {
                lastError = ex;
                watcher.EventArrived -= OnEventArrived;
                watcher.Dispose();
            }
        }

        if (_watchers.Count == 0)
            throw new InvalidOperationException("订阅散热模式事件失败。", lastError);
    }

    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        e.NewEvent.Dispose();
        ModeChanged?.Invoke();
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EventArrived -= OnEventArrived;
            try
            {
                watcher.Stop();
            }
            catch (ManagementException)
            {
                // 退出时 WMI 连接可能已断开，忽略
            }
            watcher.Dispose();
        }
        _watchers.Clear();
    }
}
