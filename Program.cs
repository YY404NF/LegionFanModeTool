using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace LegionFanModeTool;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        CrashLog.InstallProcessHandlers();
        TestLog.Write($"""
            ==== 程序启动 ====
            EXE：{Environment.ProcessPath}
            版本：{Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}
            系统：{RuntimeInformation.OSDescription}（{Environment.OSVersion.Version}）
            系统架构：{RuntimeInformation.OSArchitecture}，进程架构：{RuntimeInformation.ProcessArchitecture}，.NET：{Environment.Version}
            解压/应用目录：{AppContext.BaseDirectory}
            """);
        try
        {
            TestLog.Write("启动阶段：检查单实例互斥锁");
            using var mutex = new Mutex(true, @"Local\LegionFanModeTool", out var isFirstInstance);
            if (!isFirstInstance)
            {
                TestLog.Write("已有实例运行，本次启动退出");
                return;
            }

            TestLog.Write("启动阶段：准备初始化 WinRT / WinUI");
            RunApplication();
        }
        catch (Exception ex)
        {
            CrashLog.Write("程序启动或消息循环失败", ex);
            Environment.ExitCode = 1;
        }
        finally
        {
            TestLog.Write("Main：程序结束");
        }
    }

    // 延迟 JIT 此方法，WinUI 依赖加载失败也能由 Main 捕获并写入日志。
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunApplication()
    {
        TestLog.Write("启动阶段：初始化 WinRT COM 包装器");
        WinRT.ComWrappersSupport.InitializeComWrappers();
        TestLog.Write("启动阶段：进入 WinUI Application.Start");
        Application.Start(static parameters =>
        {
            TestLog.Write("启动阶段：创建 UI 同步上下文");
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            TestLog.Write("启动阶段：创建 App");
            _ = new App();
            TestLog.Write("启动阶段：App 创建完成");
        });
    }
}
