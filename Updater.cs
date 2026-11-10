using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace LegionFanModeTool;

/// <summary>
/// 自动更新：启动 1 分钟后检查一次，之后每 6 小时检查一次 GitHub 上最新的正式 Release；
/// 右键托盘图标时也会检查一次（<see cref="CheckNow"/>，两次检查至少间隔 1 分钟）。
/// 有更高版本时下载与本程序同类型的附件（普通版 / 带运行时版）到 "程序路径.new"，按 Release 提供的 SHA-256 校验。
/// 连不上 GitHub、超时或任何一步出错都直接放弃，下次再试，不打扰用户。
/// 正式版遇到更高的版本才更新；测试版（如 1.0.5t）遇到同级或更高的正式版（1.0.5 及以上）就更新为正式版。
/// <para>
/// 替换分三步交接，每个进程都不改动自己正在运行的文件（单文件程序按路径延迟加载程序集，
/// 运行中改名会导致后续加载失败）：本进程启动 .new 并退出 → .new 等本进程退出后把自己复制到原路径，
/// 再启动原路径 → 新版本删除 .new。
/// </para>
/// </summary>
internal sealed class Updater : IDisposable
{
    private const string GitHubLatestReleaseApi = "https://api.github.com/repos/YY404NF/LegionFanModeTool/releases/latest";

#if DIAGNOSTICS
    /// <summary>测试版可用环境变量 LFMT_UPDATE_API 指向本地模拟的 Release，用于测试更新流程；正式版没有这个入口。</summary>
    private static string LatestReleaseApi => Environment.GetEnvironmentVariable("LFMT_UPDATE_API") ?? GitHubLatestReleaseApi;
#else
    private const string LatestReleaseApi = GitHubLatestReleaseApi;
#endif
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan MinCheckInterval = TimeSpan.FromMinutes(1);

    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _checking = new(1, 1);
    private bool _enabled;
    private bool _downloaded;
    private DateTime _lastCheck = DateTime.MinValue;

    /// <summary>".new" 进程的参数：旧进程 PID 和要替换的程序路径。</summary>
    private const string ReplaceArgument = "--replace";

    /// <summary>替换完成后新版本的参数：".new" 进程的 PID，等它退出后删除 .new。</summary>
    private const string AfterUpdateArgument = "--after-update";

    private static readonly TimeSpan ProcessExitWait = TimeSpan.FromSeconds(15);
    private const int CopyAttempts = 20;
    private static readonly TimeSpan CopyRetryDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>新版本已下载并校验，调用 <see cref="LaunchReplacement"/> 后退出即可完成更新；在线程池线程上触发。</summary>
    public event Action? Downloaded;

    /// <summary>当前程序路径。</summary>
    private static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException("无法获取程序路径");

    /// <summary>下载的新版本放在程序旁边。</summary>
    private static string NewPath => ExePath + ".new";

    public void Start()
    {
        _enabled = true;
        _ = RunAsync(_cts.Token);
    }

    /// <summary>立即检查一次（右键托盘图标时调用）；未启用自动更新或距上次检查不到 1 分钟时不检查。</summary>
    public void CheckNow()
    {
        if (_enabled)
            _ = CheckAsync(_cts.Token);
    }

    /// <summary>检查并下载；同一时间只进行一次，已下载新版本后不再检查。</summary>
    private async Task CheckAsync(CancellationToken cancellation)
    {
        if (!await _checking.WaitAsync(0, cancellation))
            return;
        try
        {
            if (_downloaded || !Settings.AutoUpdate || DateTime.UtcNow - _lastCheck < MinCheckInterval)
                return;

            _lastCheck = DateTime.UtcNow;
            if (await TryDownloadAsync(cancellation))
            {
                _downloaded = true;
                Downloaded?.Invoke();
            }
        }
        finally
        {
            _checking.Release();
        }
    }

    /// <summary>
    /// 在 Main 开头、初始化界面之前调用，处理更新交接的启动参数。
    /// 返回 true 表示本进程是 ".new" 交接进程，已完成替换并启动了新版本，应直接退出。
    /// </summary>
    public static bool HandleStartup(string[] args)
    {
        switch (args)
        {
            case [ReplaceArgument, var pidText, var target] when int.TryParse(pidText, out var pid):
                // 只接受"本进程就是 目标路径.new"，避免参数被用来复制到别处
                if (!string.Equals(ExePath, target + ".new", StringComparison.OrdinalIgnoreCase))
                {
                    TestLog.Write($"自动更新：交接参数与程序路径不符，忽略（{target}）");
                    return true;
                }
                ReplaceTarget(pid, target);
                return true;

            case [AfterUpdateArgument, var pidText] when int.TryParse(pidText, out var pid):
                TestLog.Write($"自动更新：已更新到 {AppInfo.Version}，等待交接进程 {pid} 退出");
                WaitForExit(pid);
                break;
        }

        try
        {
            File.Delete(NewPath); // 交接完成后的残留，或上次下载到一半的文件
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TestLog.Write("自动更新：删除 .new 文件失败，下次启动再删", ex);
        }
        return false;
    }

    /// <summary>交接进程：等旧进程退出，把自己复制到原路径，启动原路径。复制失败时仍启动原路径（旧版本），程序不会消失。</summary>
    private static void ReplaceTarget(int oldPid, string target)
    {
        TestLog.Write($"自动更新：交接进程启动，等待旧进程 {oldPid} 退出");
        WaitForExit(oldPid);
        for (var attempt = 1; attempt <= CopyAttempts; attempt++)
        {
            try
            {
                File.Copy(ExePath, target, overwrite: true);
                TestLog.Write($"自动更新：已把新版本复制到 {target}");
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 旧进程刚退出时文件可能还被占用
                if (attempt == CopyAttempts)
                    TestLog.Write("自动更新：复制新版本失败，继续运行旧版本", ex);
                else
                    Thread.Sleep(CopyRetryDelay);
            }
        }
        Launch(target, AfterUpdateArgument, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
    }

    private static void WaitForExit(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.WaitForExit(ProcessExitWait);
        }
        catch (ArgumentException)
        {
            // 已经退出
        }
    }

    private async Task RunAsync(CancellationToken cancellation)
    {
        try
        {
            await Task.Delay(FirstCheckDelay, cancellation);
            using var timer = new PeriodicTimer(CheckInterval);
            do
            {
                await CheckAsync(cancellation);
            }
            while (!_downloaded && await timer.WaitForNextTickAsync(cancellation));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // 程序退出
        }
    }

    /// <summary>检查并下载新版本到 <see cref="NewPath"/>；已下载并校验返回 true，没有新版本或失败返回 false。</summary>
    private static async Task<bool> TryDownloadAsync(CancellationToken cancellation)
    {
        var newPath = NewPath;
        try
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // 超时由各步骤的 CancellationToken 控制
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LegionFanModeTool", AppInfo.Version));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            if (await FindUpdateAsync(http, cancellation) is not { } update)
                return false;

            TestLog.Write($"自动更新：发现新版本 {update.Version}，开始下载 {update.Url}");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(DownloadTimeout);
                await DownloadAsync(http, update, newPath, timeout.Token);
            }
            TestLog.Write($"自动更新：{update.Version} 已下载并通过 SHA-256 校验");
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException
                                       or IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            if (!cancellation.IsCancellationRequested)
                TestLog.Write("自动更新：本次检查或下载失败，下次再试", ex);
            try
            {
                File.Delete(newPath);
            }
            catch (Exception deleteError) when (deleteError is IOException or UnauthorizedAccessException)
            {
                // 留到下次覆盖或启动时删除
            }
            return false;
        }
    }

    private sealed record Update(Version Version, Uri Url, long Size, byte[] Sha256);

    /// <summary>读取最新的正式 Release；版本不比当前高、或缺少同类型附件及其 SHA-256 时返回 null。</summary>
    private static async Task<Update?> FindUpdateAsync(HttpClient http, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(QueryTimeout);
        await using var stream = await http.GetStreamAsync(LatestReleaseApi, timeout.Token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        var release = json.RootElement;

        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()
            || !Version.TryParse(release.GetProperty("tag_name").GetString()?.TrimStart('v'), out var latest)
            || !IsUpdate(latest))
        {
            TestLog.Write($"自动更新：没有新版本（当前 {AppInfo.Version}，最新 {release.GetProperty("tag_name").GetString()}）");
            return null;
        }

        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != AppInfo.ReleaseAssetName)
                continue;

            // GitHub 为每个附件提供 "sha256:十六进制" 摘要，没有摘要就不安装
            if (asset.TryGetProperty("digest", out var digest) && digest.GetString() is ['s', 'h', 'a', '2', '5', '6', ':', .. var hex])
            {
                return new Update(latest, new Uri(asset.GetProperty("browser_download_url").GetString()!),
                    asset.GetProperty("size").GetInt64(), Convert.FromHexString(hex));
            }
            TestLog.Write($"自动更新：{latest} 的附件 {AppInfo.ReleaseAssetName} 没有 SHA-256 摘要，跳过");
            return null;
        }

        TestLog.Write($"自动更新：{latest} 中没有 {AppInfo.ReleaseAssetName}，跳过");
        return null;
    }

    /// <summary>正式版要求更高的版本；测试版遇到同级的正式版也更新，把测试版换成对应的正式版。</summary>
    private static bool IsUpdate(Version latest) =>
        AppInfo.IsTestBuild ? latest >= AppInfo.CoreVersion : latest > AppInfo.CoreVersion;

    /// <summary>下载到 <paramref name="path"/> 并校验大小和 SHA-256，不一致时抛出 <see cref="InvalidDataException"/>。</summary>
    private static async Task DownloadAsync(HttpClient http, Update update, string path, CancellationToken cancellation)
    {
        using var response = await http.GetAsync(update.Url, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellation);

        await using (var file = File.Create(path))
            await source.CopyToAsync(file, cancellation);

        await using var downloaded = File.OpenRead(path);
        if (downloaded.Length != update.Size || !(await SHA256.HashDataAsync(downloaded, cancellation)).AsSpan().SequenceEqual(update.Sha256))
            throw new InvalidDataException("下载的文件大小或 SHA-256 与 Release 不一致");
    }

    /// <summary>启动已下载的 .new 交接进程，由它在本进程退出后完成替换。</summary>
    public static void LaunchReplacement() =>
        Launch(NewPath, ReplaceArgument, Environment.ProcessId.ToString(CultureInfo.InvariantCulture), ExePath);

    private static void Launch(string path, params ReadOnlySpan<string> arguments)
    {
        var startInfo = new ProcessStartInfo(path) { UseShellExecute = false }; // 继承本进程的管理员权限，不再弹出 UAC
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var _ = Process.Start(startInfo);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        // _checking 没有用到等待句柄，不需要释放；退出时进行中的检查结束后还会调用它的 Release
    }
}
