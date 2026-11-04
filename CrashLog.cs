using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace LegionFanModeTool;

/// <summary>
/// 记录未处理的异常到 %LOCALAPPDATA%\LegionFanModeTool\crash.log（超过 256 KB 时转存为 crash.old.log）。
/// XAML 层的异常记录后标记为已处理，避免程序因此退出。
/// </summary>
internal static class CrashLog
{
    private const long MaxSize = 256 * 1024;

    private static readonly string Directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionFanModeTool");

    public static string FilePath { get; } = Path.Combine(Directory, "crash.log");

    /// <summary>挂接进程级的未处理异常（应在 Main 开头调用）。</summary>
    public static void InstallProcessHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("AppDomain.UnhandledException（进程即将退出）", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>挂接 WinUI 的未处理异常：记录后标记为已处理，程序继续运行。</summary>
    public static void InstallXamlHandler(Application app) =>
        app.UnhandledException += (_, e) =>
        {
            // e.Message 保留了原始错误信息，跨越 WinRT 边界后 e.Exception 里可能丢失
            Write($"Application.UnhandledException（已处理）：{e.Message}", e.Exception);
            e.Handled = true;
        };

    public static void Write(string source, Exception? exception)
    {
        var entry = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}").AppendLine();
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            entry.Append(CultureInfo.InvariantCulture, $"{ex.GetType().FullName} (0x{ex.HResult:X8}): {ex.Message}").AppendLine()
                 .AppendLine(ex.StackTrace);
        }
        entry.AppendLine();

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxSize)
                File.Move(FilePath, Path.ChangeExtension(FilePath, ".old.log"), overwrite: true);
            File.AppendAllText(FilePath, entry.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写日志失败时不能再抛出
        }
    }
}
