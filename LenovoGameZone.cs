using System;
using System.Globalization;
using System.Linq;
using System.Management;

namespace LegionFanModeTool;

/// <summary>
/// 通过 BIOS 提供的 WMI 接口 LENOVO_GAMEZONE_DATA（root\WMI）读写散热模式，需要管理员权限。
/// </summary>
internal static class LenovoGameZone
{
    private const string Namespace = @"\\.\root\WMI";

    public static FanMode GetMode() => Invoke("读取散热模式失败", ReadMode);

    /// <summary>切换散热模式，返回切换后实际读到的模式。</summary>
    public static FanMode SetMode(FanMode mode)
    {
        if (mode is not (FanMode.Quiet or FanMode.Balanced or FanMode.Performance))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "无效的散热模式。");

        return Invoke("设置散热模式失败", gameZone =>
        {
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
        scope.Connect();
        return scope;
    }

    private static FanMode ReadMode(ManagementObject gameZone)
    {
        using var result = gameZone.InvokeMethod("GetSmartFanMode", null, null);
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
