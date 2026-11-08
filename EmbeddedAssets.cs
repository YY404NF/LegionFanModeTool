using System.Reflection;

namespace LegionFanModeTool;

/// <summary>嵌入到程序里的 Assets 资源（见 csproj 中的 EmbeddedResource）。</summary>
internal static class EmbeddedAssets
{
    public static Stream Open(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"缺少资源 {name}");
}
