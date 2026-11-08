using System.Drawing;

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
    extension(FanMode mode)
    {
        public string DisplayName => mode switch
        {
            FanMode.Quiet => "安静",
            FanMode.Balanced => "均衡",
            FanMode.Performance => "野兽",
            _ => "未知",
        };

        /// <summary>左键单击时切换到的下一个模式：安静 → 均衡 → 野兽 → 安静。</summary>
        public FanMode Next => mode switch
        {
            FanMode.Quiet => FanMode.Balanced,
            FanMode.Balanced => FanMode.Performance,
            _ => FanMode.Quiet,
        };

        /// <summary>品牌色，取自联想电脑管家散热模式页面；均衡和未知模式没有，选中颜色由 <see cref="Theme"/> 决定。</summary>
        public Color? BrandColor => mode switch
        {
            FanMode.Quiet => Color.FromArgb(0x25, 0x7E, 0xFF),
            FanMode.Performance => Color.FromArgb(0xFF, 0x52, 0x52),
            _ => null,
        };

        /// <summary>打开嵌入的图案 SVG（Assets 目录，还原自联想电脑管家）。未知模式使用均衡的图案。</summary>
        public Stream OpenSvg() => EmbeddedAssets.Open($"{mode.AssetName}.svg");

        /// <summary>打开嵌入的屏幕提示图片（Assets/Osd 目录，取自联想 Fn 热键程序）。</summary>
        public Stream OpenOsdImage() => EmbeddedAssets.Open($"osd-{mode.AssetName}.png");

        private string AssetName => mode switch
        {
            FanMode.Quiet => "quiet",
            FanMode.Performance => "performance",
            _ => "balanced",
        };
    }
}
