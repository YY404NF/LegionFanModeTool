using Microsoft.Win32;

namespace LegionFanModeTool;

/// <summary>
/// 首次启动时让托盘图标直接显示在任务栏上，而不是收在"隐藏的图标"里。
/// Windows 11 按程序路径在 HKCU\Control Panel\NotifyIconSettings 下为每个托盘图标建一个条目，
/// IsPromoted=1 表示显示在任务栏上，资源管理器会实时应用。条目在图标第一次添加后才出现，所以稍后重试几次。
/// 只做一次：条目已有 IsPromoted 值（用户调整过）时不改动，之后也不再处理。
/// </summary>
internal static class TrayPlacement
{
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";
    private const int Attempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public static void PromoteOnce()
    {
        if (Settings.TrayIconPromoted)
            return;

        _ = Task.Run(async () =>
        {
            for (var i = 0; i < Attempts; i++)
            {
                await Task.Delay(RetryDelay);
                try
                {
                    if (TryPromote())
                    {
                        Settings.TrayIconPromoted = true;
                        return;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    CrashLog.Write("设置托盘图标显示位置失败", ex);
                    return;
                }
            }
            TestLog.Write("托盘图标：没有找到本程序的通知区域条目，下次启动再试");
        });
    }

    /// <summary>找到本程序的条目后返回 true（无论是否改动）。</summary>
    private static bool TryPromote()
    {
        using var root = Registry.CurrentUser.OpenSubKey(SettingsKey);
        if (root is null)
            return false;

        foreach (var name in root.GetSubKeyNames())
        {
            using var entry = root.OpenSubKey(name, writable: true);
            if (entry?.GetValue("ExecutablePath") is not string path
                || !string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (entry.GetValue("IsPromoted") is null)
            {
                entry.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                TestLog.Write("托盘图标：首次启动，已设为显示在任务栏上");
            }
            else
            {
                TestLog.Write("托盘图标：用户已调整过显示位置，不改动");
            }
            return true;
        }
        return false;
    }
}
