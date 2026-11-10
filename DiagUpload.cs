using System.Diagnostics;
#if DIAGNOSTICS
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
#endif

namespace LegionFanModeTool;

/// <summary>
/// 测试版诊断上传：<see cref="TestLog"/> 的日志先缓存在内存，在启动完成、切换模式、出现异常和退出时
/// 打包上传到 https://diag.404nf.com/lfmt/v1/report（服务端见 diag-worker）。
/// 网络失败或服务器出错时存到 %LOCALAPPDATA%\LegionFanModeTool\pending，下次启动补传；
/// 服务器拒绝（如超出频率限制）时直接丢弃。不收集序列号、UUID、账号，每台机器只用一个随机安装 ID 区分。
/// 只在 -p:Diagnostics=true 构建时生效：正式版没有 DIAGNOSTICS 符号，所有调用在编译时被移除。
/// </summary>
internal static class DiagUpload
{
    /// <summary>追加一条日志到待上传缓冲区。</summary>
    [Conditional("DIAGNOSTICS")]
    public static void Append(string entry, bool isError)
    {
#if DIAGNOSTICS
        lock (BufferLock)
        {
            Buffer.Append(entry);
            _bufferHasError |= isError;
        }
#endif
    }

    /// <summary>稍后上传（切换模式等）：几秒内的连续操作合并成一次。</summary>
    [Conditional("DIAGNOSTICS")]
    public static void Schedule(string trigger)
    {
#if DIAGNOSTICS
        lock (BufferLock)
        {
            _scheduledTrigger = trigger;
            _debounce ??= new Timer(_ => _ = FlushAsync(_scheduledTrigger ?? "switch"));
            _debounce.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
#endif
    }

    /// <summary>立即上传（出现异常时），不等待结果。</summary>
    [Conditional("DIAGNOSTICS")]
    public static void FlushNow(string trigger)
    {
#if DIAGNOSTICS
        _ = FlushAsync(trigger);
#endif
    }

    /// <summary>同步上传并最多等待几秒（退出、启动失败、进程崩溃时，进程随后就会结束）。</summary>
    [Conditional("DIAGNOSTICS")]
    public static void FlushAndWait(string trigger)
    {
#if DIAGNOSTICS
        try
        {
            Task.Run(() => FlushAsync(trigger)).Wait(FinalFlushWait);
        }
        catch (AggregateException)
        {
            // 上传失败时已存为待补传
        }
#endif
    }

    /// <summary>补传上次没有发出去的诊断信息（启动时调用，在后台执行）。</summary>
    [Conditional("DIAGNOSTICS")]
    public static void SendPending()
    {
#if DIAGNOSTICS
        _ = Task.Run(SendPendingAsync);
#endif
    }

    /// <summary>首次启动时提示测试者：测试版会上传诊断信息。</summary>
    [Conditional("DIAGNOSTICS")]
    public static void NotifyFirstRun(TrayIcon trayIcon)
    {
#if DIAGNOSTICS
        if (_isFirstRun)
            trayIcon.ShowInfo("测试版会上传诊断信息", "包括系统版本、机型、BIOS 接口状态和运行日志，用于排查问题；不含序列号和账号信息。");
#endif
    }

#if DIAGNOSTICS
    private static readonly Uri Endpoint = new("https://diag.404nf.com/lfmt/v1/report");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FinalFlushWait = TimeSpan.FromSeconds(5);
    private const int MaxPendingFiles = 20;
    private const string InstallIdValue = "DiagInstallId";

    private static readonly string PendingDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionFanModeTool", "pending");

    private static readonly object BufferLock = new();
    private static readonly StringBuilder Buffer = new();
    private static bool _bufferHasError;
    private static string? _scheduledTrigger;
    private static Timer? _debounce;

    // 同一时间只上传一次，保证日志按顺序到达
    private static readonly SemaphoreSlim SendGate = new(1, 1);

    // 先直连（接口在 Cloudflare，国内可直连），失败再走系统代理：代理软件未运行或规则不通时不影响上传
    private static readonly HttpClient Direct = CreateClient(useProxy: false);
    private static readonly HttpClient Proxied = CreateClient(useProxy: true);

    private static bool _isFirstRun;
    private static readonly string InstallId = LoadInstallId();

    private static HttpClient CreateClient(bool useProxy)
    {
        var http = new HttpClient(new SocketsHttpHandler { UseProxy = useProxy }) { Timeout = RequestTimeout };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LegionFanModeTool", AppInfo.Version));
        return http;
    }

    /// <summary>读取或生成安装 ID（保存在 HKCU\Software\LegionFanModeTool，只有测试版使用）。</summary>
    private static string LoadInstallId()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\LegionFanModeTool");
            if (key.GetValue(InstallIdValue) is string saved && Guid.TryParse(saved, out var id))
                return id.ToString();

            _isFirstRun = true;
            var created = Guid.NewGuid().ToString();
            key.SetValue(InstallIdValue, created, RegistryValueKind.String);
            return created;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Guid.NewGuid().ToString();
        }
    }

    private static async Task FlushAsync(string trigger)
    {
        string log;
        bool hasError;
        lock (BufferLock)
        {
            if (Buffer.Length == 0)
                return;
            log = Buffer.ToString();
            hasError = _bufferHasError;
            Buffer.Clear();
            _bufferHasError = false;
            _scheduledTrigger = null;
        }

        var payload = BuildPayload(trigger, hasError, log);
        await SendGate.WaitAsync();
        try
        {
            if (await SendAsync(payload) == SendResult.Retry)
                SavePending(payload);
        }
        finally
        {
            SendGate.Release();
        }
    }

    private static async Task SendPendingAsync()
    {
        string[] files;
        try
        {
            files = Directory.Exists(PendingDirectory) ? Directory.GetFiles(PendingDirectory, "*.json.gz") : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        Array.Sort(files, StringComparer.Ordinal); // 文件名是时间戳，按时间先后补传
        await SendGate.WaitAsync();
        try
        {
            foreach (var file in files)
            {
                var result = await SendAsync(await File.ReadAllBytesAsync(file));
                if (result == SendResult.Retry)
                    return; // 仍然连不上，留到下次
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 下次再补传
        }
        finally
        {
            SendGate.Release();
        }
    }

    private enum SendResult { Sent, Rejected, Retry }

    /// <summary>上传一份已压缩的 JSON：先直连，连不上再走系统代理。</summary>
    private static async Task<SendResult> SendAsync(byte[] payload)
    {
        var result = await SendAsync(Direct, payload);
        return result == SendResult.Retry ? await SendAsync(Proxied, payload) : result;
    }

    /// <summary>网络失败、超时或服务器出错返回 Retry；服务器拒绝（4xx）返回 Rejected，不再重试。</summary>
    private static async Task<SendResult> SendAsync(HttpClient http, byte[] payload)
    {
        try
        {
            using var content = new ByteArrayContent(payload);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            content.Headers.ContentEncoding.Add("gzip");
            using var response = await http.PostAsync(Endpoint, content);
            if (response.IsSuccessStatusCode)
                return SendResult.Sent;
            Debug.WriteLine($"诊断上传被拒绝：{(int)response.StatusCode}");
            return (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout
                ? SendResult.Retry
                : SendResult.Rejected;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return SendResult.Retry;
        }
    }

    private static void SavePending(byte[] payload)
    {
        try
        {
            Directory.CreateDirectory(PendingDirectory);
            var files = Directory.GetFiles(PendingDirectory, "*.json.gz");
            if (files.Length >= MaxPendingFiles)
            {
                Array.Sort(files, StringComparer.Ordinal);
                File.Delete(files[0]); // 只保留最近的若干份
            }
            var name = $"{DateTime.UtcNow.Ticks.ToString("D20", CultureInfo.InvariantCulture)}-{Environment.ProcessId}.json.gz";
            File.WriteAllBytes(Path.Combine(PendingDirectory, name), payload);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 无法暂存时放弃这一份
        }
    }

    /// <summary>组装上报内容并 gzip 压缩，字段与服务端 schema.sql 对应。</summary>
    private static byte[] BuildPayload(string trigger, bool hasError, string log)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var json = new Utf8JsonWriter(gzip))
        {
            json.WriteStartObject();
            json.WriteString("installId", InstallId);
            json.WriteString("version", AppInfo.Version);
            json.WriteString("flavor", AppInfo.WithRuntime ? "runtime" : "normal");
            json.WriteString("trigger", trigger);
            json.WriteBoolean("hasError", hasError);
            WriteOptional(json, "model", Bios.Value.Model);
            WriteOptional(json, "bios", Bios.Value.Version);
            WriteOptional(json, "windows", Windows.Value);
            json.WriteString("log", log);
            json.WriteEndObject();
        }
        return output.ToArray();
    }

    private static void WriteOptional(Utf8JsonWriter json, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            json.WriteString(name, value.Length <= 256 ? value : value[..256]);
    }

    private static readonly Lazy<(string? Model, string? Version)> Bios = new(() =>
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        return (key?.GetValue("SystemVersion") as string, key?.GetValue("BIOSVersion") as string);
    });

    private static readonly Lazy<string?> Windows = new(() => WindowsDescription());

    /// <summary>
    /// Windows 版本描述，如 "Windows 11 Pro 26H2 (26300.9818)"。注册表的 ProductName 在 Windows 11 上仍写着
    /// "Windows 10"，按内部版本号（22000 起为 Windows 11）修正。
    /// </summary>
    internal static string? WindowsDescription()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (key is null)
            return null;
        var build = key.GetValue("CurrentBuild") as string;
        var name = key.GetValue("ProductName") as string ?? "Windows";
        if (int.TryParse(build, out var number) && number >= 22000)
            name = name.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
        return $"{name} {key.GetValue("DisplayVersion")} ({build}.{key.GetValue("UBR")})";
    }
#endif
}
