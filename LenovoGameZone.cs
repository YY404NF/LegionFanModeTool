using System;
using System.Globalization;
using System.Linq;
using System.Management;

namespace LegionFanModeTool;

/// <summary>风扇转速（RPM）与 CPU/GPU 温度（°C）。</summary>
internal readonly record struct SensorReadings(int Fan1Rpm, int Fan2Rpm, int CpuTemp, int GpuTemp);

/// <summary>
/// 通过 BIOS 提供的 WMI 接口（root\WMI）读写散热模式和风扇，需要管理员权限：
/// LENOVO_GAMEZONE_DATA 负责散热模式，LENOVO_FAN_METHOD 负责风扇转速和传感器温度。
/// （LENOVO_FAN_METHOD 的 Fan_Set_FullSpeed 在 R9000P 2021 上调用不报错但不生效，所以没有用。）
/// </summary>
internal static class LenovoGameZone
{
    private const string Namespace = @"\\.\root\WMI";
    private const string GameZoneClass = "LENOVO_GAMEZONE_DATA";
    private const string FanClass = "LENOVO_FAN_METHOD";

    // Fan_GetCurrentSensorTemperature 的传感器编号
    private const byte CpuSensorId = 3;
    private const byte GpuSensorId = 4;

    public static FanMode GetMode() => Invoke(GameZoneClass, "读取散热模式失败", ReadMode);

    /// <summary>切换散热模式，返回切换后实际读到的模式。</summary>
    public static FanMode SetMode(FanMode mode)
    {
        if (mode is not (FanMode.Quiet or FanMode.Balanced or FanMode.Performance))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "无效的散热模式。");

        return Invoke(GameZoneClass, "设置散热模式失败", gameZone =>
        {
            using (var parameters = gameZone.GetMethodParameters("SetSmartFanMode"))
            {
                parameters["Data"] = (int)mode;
                gameZone.InvokeMethod("SetSmartFanMode", parameters, null)?.Dispose();
            }
            return ReadMode(gameZone);
        });
    }

    public static SensorReadings GetSensors() => Invoke(FanClass, "读取风扇和温度失败", fan => new SensorReadings(
        ReadUInt16(fan, "Fan_GetCurrentFanSpeed", "FanID", 0, "CurrentFanSpeed"),
        ReadUInt16(fan, "Fan_GetCurrentFanSpeed", "FanID", 1, "CurrentFanSpeed"),
        ReadUInt16(fan, "Fan_GetCurrentSensorTemperature", "SensorID", CpuSensorId, "CurrentSensorTemperature"),
        ReadUInt16(fan, "Fan_GetCurrentSensorTemperature", "SensorID", GpuSensorId, "CurrentSensorTemperature")));

    private static FanMode ReadMode(ManagementObject gameZone)
    {
        using var result = gameZone.InvokeMethod("GetSmartFanMode", null, null);
        return (FanMode)Convert.ToInt32(result["Data"], CultureInfo.InvariantCulture);
    }

    private static int ReadUInt16(ManagementObject obj, string method, string inName, byte inValue, string outName)
    {
        using var parameters = obj.GetMethodParameters(method);
        parameters[inName] = inValue;
        using var result = obj.InvokeMethod(method, parameters, null);
        return Convert.ToInt32(result[outName], CultureInfo.InvariantCulture);
    }

    private static T Invoke<T>(string className, string error, Func<ManagementObject, T> action)
    {
        try
        {
            using var instance = Open(className);
            return action(instance);
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

    internal static ManagementScope ConnectScope()
    {
        var scope = new ManagementScope(Namespace, new ConnectionOptions
        {
            Impersonation = ImpersonationLevel.Impersonate,
            EnablePrivileges = true,
        });
        scope.Connect();
        return scope;
    }

    private static ManagementObject Open(string className)
    {
        using var searcher = new ManagementObjectSearcher(ConnectScope(), new ObjectQuery($"SELECT * FROM {className}"));
        using var instances = searcher.Get();
        return instances.OfType<ManagementObject>().FirstOrDefault()
            ?? throw new InvalidOperationException($"未找到联想散热控制接口（{className}），当前机型或 BIOS 可能不支持。");
    }
}
