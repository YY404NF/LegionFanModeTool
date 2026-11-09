using System.Globalization;
using Microsoft.Win32;

namespace LegionFanModeTool;

/// <summary>用户设置，保存在 HKCU\Software\LegionFanModeTool。</summary>
internal static class Settings
{
    private const string KeyPath = @"Software\LegionFanModeTool";

    /// <summary>切换模式后是否显示屏幕提示。</summary>
    public static bool ShowOsd
    {
        get => GetFlag(nameof(ShowOsd));
        set => SetFlag(nameof(ShowOsd), value);
    }

    /// <summary>是否启用 Alt+Q 全局热键，默认关闭。</summary>
    public static bool AltQHotkey
    {
        get => GetFlag(nameof(AltQHotkey), defaultValue: false);
        set => SetFlag(nameof(AltQHotkey), value);
    }

    /// <summary>
    /// 左键单击托盘图标和 Alt+Q 循环切换的模式，保存为逗号分隔的模式数值（空字符串表示一个都不切换）；
    /// 没有保存过时为 null，表示菜单中的模式全部参与。
    /// </summary>
    public static FanMode[]? CycleModes
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(nameof(CycleModes)) is string value
                ? [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(v => (FanMode)int.Parse(v, CultureInfo.InvariantCulture))]
                : null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue(nameof(CycleModes), string.Join(',', (value ?? []).Select(m => (int)m)), RegistryValueKind.String);
        }
    }

    private static bool GetFlag(string name, bool defaultValue = true)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(name, defaultValue ? 1 : 0) is int value ? value != 0 : defaultValue;
    }

    private static void SetFlag(string name, bool value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
    }
}
