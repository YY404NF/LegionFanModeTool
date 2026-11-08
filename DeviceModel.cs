using System.Collections.Frozen;
using Microsoft.Win32;

namespace LegionFanModeTool;

/// <summary>菜单顶部显示的型号：主名称和铭牌（年份/代号，铭牌可以没有）。</summary>
internal sealed record DeviceName(string Model, string? Badge);

/// <summary>
/// 设备型号，显示在右键菜单顶部。读取 BIOS 上报的产品名（与 WMI Win32_ComputerSystemProduct.Version 相同），
/// 再经过 <see cref="DisplayNames"/> 映射成显示用的名称；没有映射的型号不显示。
/// </summary>
internal static class DeviceModel
{
    private const string BiosKeyPath = @"HARDWARE\DESCRIPTION\System\BIOS";

    /// <summary>BIOS 产品名 → 显示名称。新机型在这里补充；键不区分大小写。</summary>
    private static readonly FrozenDictionary<string, DeviceName> DisplayNames = new Dictionary<string, DeviceName>
    {
        ["Lenovo Legion R9000P2021H"] = new("R9000P", "2021H"),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>显示用的型号，首次访问时读取一次；读取不到或没有映射时为 null。</summary>
    public static DeviceName? DisplayName { get; } = ReadDisplayName();

    private static DeviceName? ReadDisplayName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(BiosKeyPath);
            // SystemVersion 是完整产品名（如 Lenovo Legion R9000P2021H），个别机型为空时退回 SystemFamily
            var name = ReadValue(key, "SystemVersion") ?? ReadValue(key, "SystemFamily");
            return name is not null && DisplayNames.TryGetValue(name, out var displayName) ? displayName : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static string? ReadValue(RegistryKey? key, string name) =>
        key?.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
