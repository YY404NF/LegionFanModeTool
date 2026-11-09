using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;

namespace LegionFanModeTool;

/// <summary>
/// 不使用 App.xaml，控件样式直接在代码中合并。
/// 依赖系统运行时的版本不需要应用资源索引；随包运行时版本通过构建生成合并的 resources.pri。
/// </summary>
[SuppressMessage("Reliability", "CA1001", Justification = "TrayApp 在用户选择退出时自行释放，Application 本身没有释放时机。")]
public sealed partial class App : Application, IXamlMetadataProvider
{
    private readonly XamlControlsXamlMetaDataProvider _provider = new();
    private TrayApp? _tray; // 持有引用，托盘程序随应用存活

    public App()
    {
        UnhandledException += (_, e) =>
        {
            CrashLog.Write($"Application.UnhandledException（已处理）：{e.Message}", e.Exception);
            e.Handled = true;
        };
        TestLog.Write("启动阶段：App 构造完成，已挂接 XAML 异常处理");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        TestLog.Write("启动阶段：加载 WinUI 控件资源");
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        TestLog.Write("启动阶段：创建托盘程序、菜单窗口与模式监听器");
        _tray = new TrayApp();
        TestLog.Write("启动阶段：托盘程序创建完成");
    }

    public IXamlType GetXamlType(Type type) => _provider.GetXamlType(type);
    public IXamlType GetXamlType(string fullName) => _provider.GetXamlType(fullName);
    public XmlnsDefinition[] GetXmlnsDefinitions() => _provider.GetXmlnsDefinitions();
}
