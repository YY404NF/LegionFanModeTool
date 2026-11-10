using System.Globalization;
using System.Management;

namespace LegionFanModeTool;

/// <summary>
/// 通过 BIOS 提供的 WMI 接口（root\WMI）读写散热模式和风扇全速开关，需要管理员权限。
/// </summary>
internal static class LenovoGameZone
{
    private const string Namespace = @"\\.\root\WMI";

    public static FanMode GetMode() => Invoke("读取散热模式失败", ReadMode);

    /// <summary>新一代接口（如 R9000P ADR10）中"支持的散热模式"的功能编号，用 LENOVO_OTHER_METHOD.GetFeatureValue 读取。</summary>
    private const uint SupportedThermalModeFeature = 0x70000;

    /// <summary>
    /// 读取固件声明支持的散热模式位图：位 0、1、2 为安静、均衡、野兽，位 4 为极致，位 16 为自定义。
    /// 旧机型（如 R9000P 2021H）用 LENOVO_OTHER_METHOD.GetSupportThermalMode；新机型没有这个方法，
    /// 改用 GetFeatureValue(0x70000)，与 Legion Zone 的判断方式一致。接口缺失或读取失败时为 null。
    /// </summary>
    public static uint? GetSupportedModeMask()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(ConnectScope(), new ObjectQuery("SELECT * FROM LENOVO_OTHER_METHOD"));
            using var instances = searcher.Get();
            using var instance = instances.OfType<ManagementObject>().FirstOrDefault();
            if (instance is null)
            {
                TestLog.Write("WMI：LENOVO_OTHER_METHOD 无实例");
                return null;
            }

            using var cls = new ManagementClass(instance.Scope, new ManagementPath("LENOVO_OTHER_METHOD"), null);
            var methods = cls.Methods.Cast<MethodData>().Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
            if (methods.Contains("GetSupportThermalMode"))
            {
                using var result = instance.InvokeMethod("GetSupportThermalMode", null, null);
                TestLog.Write($"WMI：GetSupportThermalMode 返回 mode={ReadProperty(result, "mode")}");
                return ToMask(ReadProperty(result, "mode"));
            }
            if (methods.Contains("GetFeatureValue"))
            {
                using var parameters = instance.GetMethodParameters("GetFeatureValue");
                SetUInt(parameters, "IDs", SupportedThermalModeFeature);
                using var result = instance.InvokeMethod("GetFeatureValue", parameters, null);
                TestLog.Write($"WMI：GetFeatureValue(0x{SupportedThermalModeFeature:X}) 返回 value={ReadProperty(result, "value")}");
                return ToMask(ReadProperty(result, "value"));
            }
            TestLog.Write("WMI：LENOVO_OTHER_METHOD 没有可用的散热模式查询方法");
            return null;
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.InvalidClass)
        {
            TestLog.Write("WMI：当前系统未提供 LENOVO_OTHER_METHOD");
            return null;
        }
        catch (Exception ex)
        {
            CrashLog.Write("读取支持的散热模式失败", ex);
            return null;
        }
    }

    /// <summary>固件是否声明支持自定义模式（位 16）；无法确定时不提供该选项。</summary>
    public static bool SupportsCustomMode() => GetSupportedModeMask() is { } mask && (mask & (1u << 16)) != 0;

    private static uint? ToMask(object? value) => value is null ? null : Convert.ToUInt32(value, CultureInfo.InvariantCulture);

    /// <summary>按 WMI 参数声明的类型写入整数，避免类型不匹配。</summary>
    private static void SetUInt(ManagementBaseObject parameters, string name, uint value) =>
        parameters[name] = parameters.Properties[name].Type switch
        {
            CimType.SInt32 => unchecked((int)value),
            CimType.UInt64 => (ulong)value,
            CimType.SInt64 => (long)value,
            _ => value,
        };

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

    /// <summary>新一代接口中"风扇全速"的功能编号（LENOVO_OTHER_METHOD.GetFeatureValue / SetFeatureValue）。</summary>
    private const uint FanFullSpeedFeature = 0x04020000;

    /// <summary>
    /// 风扇全速开关所在的接口：旧机型（如 R9000P 2021H）是 LENOVO_FAN_METHOD.Fan_Get/Set_FullSpeed；
    /// 新机型（如 R9000P ADR10）没有这两个方法，改用 GetFeatureValue / SetFeatureValue(0x04020000)，
    /// 与 Legion Zone、Lenovo Legion Toolkit 一致。首次使用时检测一次。
    /// </summary>
    private static readonly Lazy<bool> UsesFanMethod = new(() => HasMethod(FanMethodClass, "Fan_Get_FullSpeed"));

    /// <summary>
    /// 读取"风扇全速"开关；接口不存在或读取失败时为 null。
    /// BIOS 不把它保存在自定义档位里，切回自定义也不会恢复，Legion Zone 每次应用自定义模式时会重新写入。
    /// </summary>
    public static bool? GetFanFullSpeed()
    {
        try
        {
            if (UsesFanMethod.Value)
            {
                using var fan = Open(FanMethodClass);
                using var result = fan.InvokeMethod("Fan_Get_FullSpeed", null, null);
                TestLog.Write($"WMI：Fan_Get_FullSpeed 返回 Status={ReadProperty(result, "Status")}");
                return ReadProperty(result, "Status") is { } status ? Convert.ToBoolean(status, CultureInfo.InvariantCulture) : null;
            }

            using var other = Open(OtherMethodClass);
            using var parameters = other.GetMethodParameters("GetFeatureValue");
            SetUInt(parameters, "IDs", FanFullSpeedFeature);
            using var output = other.InvokeMethod("GetFeatureValue", parameters, null);
            var value = ReadProperty(output, "value");
            TestLog.Write($"WMI：GetFeatureValue(0x{FanFullSpeedFeature:X}) 返回 value={value}");
            return value is null ? null : Convert.ToUInt32(value, CultureInfo.InvariantCulture) != 0;
        }
        catch (Exception ex) when (ex is ManagementException or InvalidOperationException or UnauthorizedAccessException or FormatException)
        {
            TestLog.Write($"WMI：读取风扇全速开关失败：{ex.GetType().Name} {ex.Message.Trim()}");
            return null;
        }
    }

    /// <summary>设置"风扇全速"开关。</summary>
    public static void SetFanFullSpeed(bool enabled)
    {
        if (UsesFanMethod.Value)
        {
            Invoke("设置风扇全速失败", FanMethodClass, fan =>
            {
                TestLog.Write($"WMI：Fan_Set_FullSpeed 写入 Status={enabled}");
                using var parameters = fan.GetMethodParameters("Fan_Set_FullSpeed");
                parameters["Status"] = enabled;
                fan.InvokeMethod("Fan_Set_FullSpeed", parameters, null)?.Dispose();
                return enabled;
            });
            return;
        }

        Invoke("设置风扇全速失败", OtherMethodClass, other =>
        {
            TestLog.Write($"WMI：SetFeatureValue(0x{FanFullSpeedFeature:X}) 写入 value={(enabled ? 1 : 0)}");
            using var parameters = other.GetMethodParameters("SetFeatureValue");
            SetUInt(parameters, "IDs", FanFullSpeedFeature);
            SetUInt(parameters, "value", enabled ? 1u : 0u);
            other.InvokeMethod("SetFeatureValue", parameters, null)?.Dispose();
            return enabled;
        });
    }

    /// <summary>某个 WMI 类是否提供指定方法（接口缺失时为 false）。</summary>
    private static bool HasMethod(string className, string methodName)
    {
        try
        {
            using var cls = new ManagementClass(ConnectScope(), new ManagementPath(className), null);
            return cls.Methods.Cast<MethodData>().Any(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase));
        }
        catch (ManagementException)
        {
            return false;
        }
    }

    /// <summary>按名称读取输出参数，忽略大小写（Legion Zone 写作 "value"，Lenovo Legion Toolkit 写作 "Value"）。</summary>
    private static object? ReadProperty(ManagementBaseObject? result, string name) =>
        result?.Properties.Cast<PropertyData>().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

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

    private static T Invoke<T>(string error, Func<ManagementObject, T> action) => Invoke(error, GameZoneClass, action);

    private static T Invoke<T>(string error, string className, Func<ManagementObject, T> action)
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

    private const string GameZoneClass = "LENOVO_GAMEZONE_DATA";
    private const string FanMethodClass = "LENOVO_FAN_METHOD";
    private const string OtherMethodClass = "LENOVO_OTHER_METHOD";

    private static ManagementObject Open(string className)
    {
        using var searcher = new ManagementObjectSearcher(ConnectScope(), new ObjectQuery($"SELECT * FROM {className}"));
        using var instances = searcher.Get();
        return instances.OfType<ManagementObject>().FirstOrDefault()
            ?? throw new InvalidOperationException($"未找到联想 BIOS 接口（{className}），当前机型或 BIOS 可能不支持。");
    }
}
