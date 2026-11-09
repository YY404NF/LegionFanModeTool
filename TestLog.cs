using System.Diagnostics;
#if DIAGNOSTICS
using System.Globalization;
using System.Text;
#endif

namespace LegionFanModeTool;

/// <summary>测试版统一日志，追加到实际 EXE 所在目录；单文件解压目录不作为日志目录。</summary>
internal static class TestLog
{
#if DIAGNOSTICS
    private static readonly string FilePath = Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "LegionFanModeTool.log");

    // 重复启动的进程也要记录，跨进程互斥避免同时追加时丢失日志。
    private static readonly Mutex WriteMutex = new(false, @"Local\LegionFanModeTool.TestLog");
#endif

    [Conditional("DIAGNOSTICS")]
    public static void Write(string message, Exception? error = null)
    {
#if DIAGNOSTICS
        var entry = new StringBuilder()
            .Append(CultureInfo.InvariantCulture,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] [PID {Environment.ProcessId}] [线程 {Environment.CurrentManagedThreadId}] ")
            .AppendLine(message);
        if (error is not null)
            entry.Append(CultureInfo.InvariantCulture, $"HRESULT：0x{error.HResult:X8}").AppendLine()
                .AppendLine(error.ToString());
        entry.AppendLine();

        var acquired = false;
        try
        {
            try
            {
                acquired = WriteMutex.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (acquired)
                File.AppendAllText(FilePath, entry.ToString(), Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // 日志目录不可写时，不让诊断功能导致程序退出。
            Debug.WriteLine($"无法追加日志 {FilePath}：{ex}");
        }
        finally
        {
            if (acquired)
                WriteMutex.ReleaseMutex();
        }
#endif
    }
}
