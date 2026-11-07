using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;

namespace LegionFanModeTool;

/// <summary>
/// 不使用 App.xaml：控件样式直接在代码中合并。这样程序不依赖自身的 resources.pri，
/// 单文件发布时（WinUI 从已安装的运行时加载，只会在 exe 旁边找 pri）菜单也能正常显示。
/// </summary>
[SuppressMessage("Reliability", "CA1001", Justification = "TrayApp 在用户选择退出时自行释放，Application 本身没有释放时机。")]
public sealed partial class App : Application, IXamlMetadataProvider
{
    private readonly XamlControlsXamlMetaDataProvider _provider = new();
    private TrayApp? _tray; // 持有引用，托盘程序随应用存活

    public App() => CrashLog.InstallXamlHandler(this);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        _tray = new TrayApp();
    }

    public IXamlType GetXamlType(Type type) => _provider.GetXamlType(type);
    public IXamlType GetXamlType(string fullName) => _provider.GetXamlType(fullName);
    public XmlnsDefinition[] GetXmlnsDefinitions() => _provider.GetXmlnsDefinitions();
}
