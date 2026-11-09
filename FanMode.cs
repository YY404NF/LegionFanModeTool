using System.Drawing;

namespace LegionFanModeTool;

/// <summary>散热模式，数值与 LENOVO_GAMEZONE_DATA 的 SmartFanMode 一致。</summary>
internal enum FanMode
{
    Unknown = 0,
    Quiet = 1,
    Balanced = 2,
    Performance = 3,

    /// <summary>极致模式，2025 年及以后的新机型（如 R9000P ADR10）才有；只识别显示，不在菜单中提供。</summary>
    Extreme = 224,

    /// <summary>
    /// 自定义模式。BIOS 只有这一个自定义槽位，切换后沿用 Legion Zone 最后一次写入的功耗和风扇参数；
    /// Legion Zone 里各个自定义模式的名称和参数保存在联想云端的账号中，本程序不读取。
    /// </summary>
    Custom = 255,
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
            FanMode.Extreme => "极致",
            FanMode.Custom => "自定义",
            _ => "未知",
        };

        /// <summary>
        /// 品牌色：安静、野兽取自联想电脑管家散热模式页面，自定义取自 Legion Zone 的自定义模式紫色；
        /// 均衡和未知模式没有，选中颜色由 <see cref="Theme"/> 决定。
        /// </summary>
        public Color? BrandColor => mode switch
        {
            FanMode.Quiet => Color.FromArgb(0x25, 0x7E, 0xFF),
            FanMode.Performance or FanMode.Extreme => Color.FromArgb(0xFF, 0x52, 0x52),
            FanMode.Custom => Color.FromArgb(0x8C, 0x5C, 0xF5),
            _ => null,
        };

        /// <summary>打开嵌入的图案 SVG（Assets 目录，还原自联想电脑管家；自定义按 Fn 热键程序的提示图绘制）。极致使用野兽、未知使用均衡的图案。</summary>
        public Stream OpenSvg() => EmbeddedAssets.Open($"{mode.AssetName}.svg");

        /// <summary>打开嵌入的屏幕提示图片（Assets/Osd 目录，取自联想 Fn 热键程序）。</summary>
        public Stream OpenOsdImage() => EmbeddedAssets.Open($"osd-{mode.AssetName}.png");

        private string AssetName => mode switch
        {
            FanMode.Quiet => "quiet",
            FanMode.Performance or FanMode.Extreme => "performance",
            FanMode.Custom => "custom",
            _ => "balanced",
        };
    }
}
