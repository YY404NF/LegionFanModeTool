using System.Globalization;
using System.Management;

namespace LegionFanModeTool;

/// <summary>
/// 通过 BIOS 提供的 WMI 接口 LENOVO_GAMEZONE_DATA（root\WMI）读写散热模式，需要管理员权限。
/// </summary>
internal static class LenovoGameZone
{
    private const string Namespace = @"\\.\root\WMI";

    public static FanMode GetMode() => Invoke("读取散热模式失败", ReadMode);

    /// <summary>只读查询固件是否声明支持自定义模式；接口缺失或查询失败时不提供该选项。</summary>
    public static bool SupportsCustomMode()
    {
        try
        {
            TestLog.Write("WMI：查询 LENOVO_OTHER_METHOD.GetSupportThermalMode");
            using var searcher = new ManagementObjectSearcher(ConnectScope(), new ObjectQuery("SELECT * FROM LENOVO_OTHER_METHOD"));
            using var instances = searcher.Get();
            using var instance = instances.OfType<ManagementObject>().FirstOrDefault();
            if (instance is null)
            {
                TestLog.Write("WMI：LENOVO_OTHER_METHOD 无实例，不显示自定义模式");
                return false;
            }

            using var result = instance.InvokeMethod("GetSupportThermalMode", null, null);
            TestLog.Write($"WMI：GetSupportThermalMode 返回 mode={result?["mode"]}");
            // 位 0、1、2 分别为安静、均衡、野兽；位 16 为自定义。
            return result?["mode"] is { } value
                && (Convert.ToUInt32(value, CultureInfo.InvariantCulture) & (1u << 16)) != 0;
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.InvalidClass)
        {
            TestLog.Write("WMI：当前系统未提供 LENOVO_OTHER_METHOD，不显示自定义模式");
            return false;
        }
        catch (Exception ex)
        {
            CrashLog.Write("检测自定义模式支持情况失败", ex);
            return false;
        }
    }

    /// <summary>切换散热模式，返回切换后实际读到的模式。</summary>
    public static FanMode SetMode(FanMode mode)
    {
        if (mode is not (FanMode.Quiet or FanMode.Balanced or FanMode.Performance or FanMode.Custom))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "无效的散热模式。");

        return Invoke("设置散热模式失败", gameZone =>
        {
            TestLog.Write($"WMI：SetSmartFanMode 写入 Data={(int)mode}");
            using (var parameters = gameZone.GetMethodParameters("SetSmartFanMode"))
            {
                parameters["Data"] = (int)mode;
                gameZone.InvokeMethod("SetSmartFanMode", parameters, null)?.Dispose();
            }
            return ReadMode(gameZone);
        });
    }

    /// <summary>连接 root\WMI 命名空间，ModeWatcher 订阅事件时也用它。</summary>
    public static ManagementScope ConnectScope()
    {
        var scope = new ManagementScope(Namespace, new ConnectionOptions
        {
            Impersonation = ImpersonationLevel.Impersonate,
            EnablePrivileges = true,
        });
        TestLog.Write($"WMI：开始连接 {Namespace}");
        scope.Connect();
        TestLog.Write($"WMI：已连接 {Namespace}");
        return scope;
    }

    private static FanMode ReadMode(ManagementObject gameZone)
    {
        TestLog.Write("WMI：开始读取 GetSmartFanMode");
        using var result = gameZone.InvokeMethod("GetSmartFanMode", null, null);
        TestLog.Write($"WMI：GetSmartFanMode 返回 Data={result["Data"]}");
        return (FanMode)Convert.ToInt32(result["Data"], CultureInfo.InvariantCulture);
    }

    private static T Invoke<T>(string error, Func<ManagementObject, T> action)
    {
        try
        {
            using var gameZone = Open();
            return action(gameZone);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                       or ManagementException { ErrorCode: ManagementStatus.AccessDenied })
        {
            throw new UnauthorizedAccessException("访问被拒绝，请以管理员身份运行。", ex);
        }
        catch (ManagementException ex)
        {
            throw new InvalidOperationException($"{error}：{ex.Message}", ex);
        }
    }

    private static ManagementObject Open()
    {
        using var searcher = new ManagementObjectSearcher(ConnectScope(), new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA"));
        using var instances = searcher.Get();
        return instances.OfType<ManagementObject>().FirstOrDefault()
            ?? throw new InvalidOperationException("未找到联想散热控制接口（LENOVO_GAMEZONE_DATA），当前机型或 BIOS 可能不支持。");
    }
}
