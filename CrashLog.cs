using System.Globalization;
using System.Text;

namespace LegionFanModeTool;

/// <summary>
/// 正式版异常保存到用户目录；测试版统一追加到 EXE 同目录的日志。
/// </summary>
internal static class CrashLog
{
#if !DIAGNOSTICS
    private const long MaxSize = 256 * 1024;

    private static readonly string Directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionFanModeTool");

    public static string FilePath { get; } = Path.Combine(Directory, "crash.log");
#endif

    /// <summary>挂接进程级的未处理异常（应在 Main 开头调用）。</summary>
    public static void InstallProcessHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Write("AppDomain.UnhandledException（进程即将退出）", e.ExceptionObject as Exception);
            DiagUpload.FlushAndWait("error"); // 进程马上结束，同步上传
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TestLog.Write("ProcessExit：进程退出");
    }

    public static void Write(string source, Exception? exception)
    {
#if DIAGNOSTICS
        TestLog.Write(source, exception);
        DiagUpload.FlushNow("error");
#else
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
#endif
    }
}
