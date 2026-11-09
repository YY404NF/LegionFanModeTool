using System.Diagnostics;
#if DIAGNOSTICS
using System.Globalization;
using System.Management;
using System.Text;
using Microsoft.Win32;
#endif

namespace LegionFanModeTool;

/// <summary>
/// 测试版专用的诊断信息：读取或切换模式后，把系统、机型和 Lenovo WMI 接口情况
/// 追加到 EXE 同目录的 LegionFanModeTool.log。
/// 只在用 -p:Diagnostics=true 构建时生效：正式版没有 DIAGNOSTICS 符号，
/// <see cref="ConditionalAttribute"/> 让编译器移除所有调用，方法体也是空的。
/// 不收集序列号、UUID、账号等个人信息。
/// </summary>
internal static class Diagnostics
{
#if DIAGNOSTICS
    /// <summary>托盘提示里的测试版标记。</summary>
    public const string Label = "（测试版）";
#else
    public const string Label = "";
#endif

    /// <summary>记录一次操作的结果并生成诊断信息。</summary>
    [Conditional("DIAGNOSTICS")]
    public static void Report(string operation, FanMode? result, Exception? error)
    {
#if DIAGNOSTICS
        _ = ReportAsync(operation, result, error);
#endif
    }

#if DIAGNOSTICS
    private static readonly SemaphoreSlim ReportGate = new(1, 1);

    private static async Task ReportAsync(string operation, FanMode? result, Exception? error)
    {
        // 串行执行只读诊断，避免连续切换时同时查询多组 WMI 接口。
        await ReportGate.WaitAsync();
        try
        {
            TestLog.Write($"开始收集诊断信息：{operation}", error);
            var text = await Task.Run(() => Collect(operation, result, error));
            TestLog.Write(text);
            TestLog.Write($"诊断信息收集完成：{operation}");
        }
        catch (Exception ex)
        {
            CrashLog.Write("生成诊断信息失败", ex);
        }
        finally
        {
            ReportGate.Release();
        }
    }

    private static string Collect(string operation, FanMode? result, Exception? error)
    {
        var sb = new StringBuilder();
        sb.AppendLine("==== LegionFanModeTool 诊断信息 ====");
        sb.AppendLine(CultureInfo.InvariantCulture, $"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"版本：{AppInfo.InformationalVersion}");

        sb.AppendLine().AppendLine("[本次操作]");
        sb.AppendLine(CultureInfo.InvariantCulture, $"操作：{operation}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"结果：{(result is { } mode ? $"{mode.DisplayName}（{(int)mode}）" : "无")}");
        if (error is not null)
            sb.AppendLine(CultureInfo.InvariantCulture, $"错误：{error}");

        sb.AppendLine().AppendLine("[系统]");
        Section(sb, () =>
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Windows：{key?.GetValue("ProductName")} {key?.GetValue("DisplayVersion")}，内部版本 {key?.GetValue("CurrentBuild")}.{key?.GetValue("UBR")}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"架构：{System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}，.NET {Environment.Version}");
        });

        sb.AppendLine().AppendLine("[机型（注册表 BIOS 信息）]");
        Section(sb, () =>
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            foreach (var name in (ReadOnlySpan<string>)["SystemManufacturer", "SystemProductName", "SystemFamily", "SystemVersion", "SystemSKU", "BIOSVendor", "BIOSVersion", "BIOSReleaseDate", "BaseBoardProduct"])
                sb.AppendLine(CultureInfo.InvariantCulture, $"{name}：{key?.GetValue(name)}");
            var model = DeviceModel.DisplayName;
            sb.AppendLine(CultureInfo.InvariantCulture, $"显示型号：{(model is null ? "（未映射，不显示）" : model.Model + " " + model.Badge)}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"已学到的自定义模式风扇全速：{Settings.CustomFanFullSpeed?.ToString() ?? "未学到"}");
        });

        sb.AppendLine().AppendLine("[Legion Zone]");
        Section(sb, () =>
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Lenovo\LegionZone");
            sb.AppendLine(key is null ? "未安装" : $"版本：{key.GetValue("Version")}");
        });

        sb.AppendLine().AppendLine("[root\\WMI 中的 LENOVO 接口]");
        Section(sb, () =>
        {
            var scope = LenovoGameZone.ConnectScope();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM meta_class WHERE __CLASS LIKE 'LENOVO%'"));
            using var classes = searcher.Get();
            foreach (var cls in classes.OfType<ManagementClass>().OrderBy(c => c.ClassPath.ClassName, StringComparer.Ordinal))
            {
                using (cls)
                {
                    var methods = cls.Methods.Cast<MethodData>().Select(m => m.Name).ToArray();
                    sb.AppendLine(methods.Length == 0 ? cls.ClassPath.ClassName : $"{cls.ClassPath.ClassName}：{string.Join(", ", methods)}");
                }
            }
        });

        // 只调用无参数的 Get*/IsSupport* 方法，都是只读查询，不改变任何设置；
        // LENOVO_OTHER_METHOD 的 GetSupportThermalMode、GetCustomModeAbility 用来判断新机型支持的模式
        foreach (var className in (ReadOnlySpan<string>)["LENOVO_GAMEZONE_DATA", "LENOVO_OTHER_METHOD", "LENOVO_FAN_METHOD"])
        {
            sb.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"[{className} 只读查询]");
            Section(sb, () => QueryReadOnlyMethods(sb, className));
        }

        return sb.ToString();
    }

    private static void QueryReadOnlyMethods(StringBuilder sb, string className)
    {
        try
        {
            QueryAvailableMethods(sb, className);
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.InvalidClass)
        {
            var message = $"{className}：当前系统未提供此可选接口";
            sb.AppendLine(message);
            TestLog.Write(message);
        }
    }

    private static void QueryAvailableMethods(StringBuilder sb, string className)
    {
        using var searcher = new ManagementObjectSearcher(LenovoGameZone.ConnectScope(), new ObjectQuery($"SELECT * FROM {className}"));
        using var instances = searcher.Get();
        using var instance = instances.OfType<ManagementObject>().FirstOrDefault()
            ?? throw new InvalidOperationException($"没有 {className} 实例");
        using var cls = new ManagementClass(instance.Scope, new ManagementPath(className), null);
        foreach (var method in cls.Methods.Cast<MethodData>().OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            if (method.InParameters is not null || !(method.Name.StartsWith("Get", StringComparison.Ordinal) || method.Name.StartsWith("IsSupport", StringComparison.Ordinal)))
                continue;
            try
            {
                TestLog.Write($"WMI 诊断查询开始：{className}.{method.Name}");
                using var output = instance.InvokeMethod(method.Name, null, null);
                var values = output.Properties.Cast<PropertyData>()
                    .Select(p => $"{p.Name}={Format(p.Value)}");
                var line = $"{method.Name}：{string.Join(", ", values)}";
                sb.AppendLine(line);
                TestLog.Write($"WMI 诊断查询完成：{className}.{line}");
            }
            catch (Exception ex)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"{method.Name}：失败 {ex.GetType().Name} {ex.Message}");
                TestLog.Write($"WMI 诊断查询失败：{className}.{method.Name}", ex);
            }
        }
    }

    /// <summary>WMI 返回值可能是数组（如字节表），展开成逗号分隔。</summary>
    private static string? Format(object? value) => value is Array array
        ? $"[{string.Join(" ", array.Cast<object>().Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)))}]"
        : Convert.ToString(value, CultureInfo.InvariantCulture);

    /// <summary>某一部分出错时只记录错误，不影响其余部分。</summary>
    private static void Section(StringBuilder sb, Action collect)
    {
        try
        {
            collect();
        }
        catch (Exception ex)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"读取失败：{ex.GetType().Name} {ex.Message}");
            TestLog.Write("读取诊断信息分组失败", ex);
        }
    }
#endif
}
