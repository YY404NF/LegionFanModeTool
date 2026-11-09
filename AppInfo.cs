using System.Reflection;

namespace LegionFanModeTool;

/// <summary>程序版本与发布类型。</summary>
internal static class AppInfo
{
    /// <summary>构建信息版本，含提交哈希，如 "1.0.3+28e5703…"，诊断日志用。</summary>
    public static string InformationalVersion { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>版本号（构建时的 -p:Version，含 dev 等预发布后缀，不含提交哈希），如 "1.0.3"、"1.0.3-dev"。</summary>
    public static string Version { get; } = InformationalVersion.Split('+')[0];

#if WITH_RUNTIME
    /// <summary>是否为自带 Windows App SDK 运行时的版本。</summary>
    public const bool WithRuntime = true;
#else
    public const bool WithRuntime = false;
#endif

    /// <summary>菜单中显示的版本，带运行时的版本加 "r"，如 "v1.0.2r"。</summary>
    public static string DisplayVersion => $"v{Version}{(WithRuntime ? "r" : "")}";

    /// <summary>GitHub Release 中与本程序同类型的附件名（发布文件名不带版本号）。</summary>
    public const string ReleaseAssetName = WithRuntime ? "LegionFanModeTool_with_runtime.exe" : "LegionFanModeTool.exe";
}
