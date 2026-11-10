using System.Reflection;

namespace LegionFanModeTool;

/// <summary>程序版本与发布类型。</summary>
internal static class AppInfo
{
    /// <summary>构建信息版本，含提交哈希，如 "1.0.3+28e5703…"，诊断日志用。</summary>
    public static string InformationalVersion { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>版本号（构建时的 -p:Version，含 dev 等预发布后缀，不含提交哈希），如 "1.0.5"、"1.0.5-dev"。</summary>
    public static string Version { get; } = InformationalVersion.Split('+')[0];

    /// <summary>去掉预发布后缀的数字版本，如 "1.0.5-dev" 为 1.0.5；用于显示和自动更新比较。</summary>
    public static Version CoreVersion { get; } =
        System.Version.TryParse(Version.Split('-')[0], out var core) ? core : new Version(0, 0, 0);

#if DIAGNOSTICS
    /// <summary>是否为测试版（带诊断上传）。</summary>
    public const bool IsTestBuild = true;
#else
    public const bool IsTestBuild = false;
#endif

#if WITH_RUNTIME
    /// <summary>是否为自带 Windows App SDK 运行时的版本。</summary>
    public const bool WithRuntime = true;
#else
    public const bool WithRuntime = false;
#endif

    /// <summary>菜单中显示的版本：带运行时加 "r"，测试版加 "t"，如 "v1.0.5"、"v1.0.5r"、"v1.0.5t"、"v1.0.5rt"。</summary>
    public static string DisplayVersion => $"v{CoreVersion}{(WithRuntime ? "r" : "")}{(IsTestBuild ? "t" : "")}";

    /// <summary>GitHub Release 中与本程序同类型的附件名（发布文件名不带版本号）。</summary>
    public const string ReleaseAssetName = WithRuntime ? "LegionFanModeTool_with_runtime.exe" : "LegionFanModeTool.exe";
}
