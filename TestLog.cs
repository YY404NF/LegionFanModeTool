using System.Diagnostics;
#if DIAGNOSTICS
using System.Globalization;
using System.Text;
#endif

namespace LegionFanModeTool;

/// <summary>
/// 测试版统一日志：记录启动阶段、接口读写结果和异常，交给 <see cref="DiagUpload"/> 缓存并上传，不再写本地文件。
/// 正式版没有 DIAGNOSTICS 符号，所有调用在编译时被移除。
/// </summary>
internal static class TestLog
{
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
        DiagUpload.Append(entry.AppendLine().ToString(), isError: error is not null);
#endif
    }
}
