using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace LegionFanModeTool;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\LegionFanModeTool", out var isFirstInstance);
        if (!isFirstInstance)
            return;

        CrashLog.InstallProcessHandlers();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(static parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
}
