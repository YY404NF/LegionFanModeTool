using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace LegionFanModeTool;

/// <summary>散热模式，数值与 LENOVO_GAMEZONE_DATA 的 SmartFanMode 一致。</summary>
internal enum FanMode
{
    Unknown = 0,
    Quiet = 1,
    Balanced = 2,
    Performance = 3,
}

internal static class FanModeExtensions
{
    /// <summary>未选中模式的图标颜色。</summary>
    public static readonly Color InactiveColor = Color.FromArgb(0x95, 0x95, 0x95);

    extension(FanMode mode)
    {
        public string DisplayName => mode switch
        {
            FanMode.Quiet => "安静",
            FanMode.Balanced => "均衡",
            FanMode.Performance => "野兽",
            _ => "未知",
        };

        /// <summary>选中时的颜色，取自联想电脑管家散热模式页面。</summary>
        public Color AccentColor => mode switch
        {
            FanMode.Quiet => Color.FromArgb(0x25, 0x7E, 0xFF),
            FanMode.Balanced => Color.White,
            FanMode.Performance => Color.FromArgb(0xFF, 0x52, 0x52),
            _ => InactiveColor,
        };

        /// <summary>打开嵌入的图案 SVG（Assets 目录，还原自联想电脑管家）。未知模式使用均衡的图案。</summary>
        public Stream OpenSvg()
        {
            var name = mode switch
            {
                FanMode.Quiet => "quiet",
                FanMode.Performance => "performance",
                _ => "balanced",
            };
            return Assembly.GetExecutingAssembly().GetManifestResourceStream($"{name}.svg")
                ?? throw new InvalidOperationException($"缺少图标资源 {name}.svg");
        }
    }
}
