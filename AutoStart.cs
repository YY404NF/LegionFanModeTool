using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using LegionFanModeTool.Interop;

namespace LegionFanModeTool;

/// <summary>
/// 通过计划任务实现开机自启。程序需要管理员权限，注册表 Run 项无法提权启动，
/// 所以使用“登录时以最高权限运行”的计划任务。
/// </summary>
internal static class AutoStart
{
    private const string TaskName = "LegionFanModeTool";

    private static string ExePath => Environment.ProcessPath
        ?? throw new InvalidOperationException("无法获取程序路径。");

    /// <summary>任务存在且指向当前 exe 时视为已启用；任务不存在或无法读取都视为未启用。</summary>
    public static bool IsEnabled()
    {
        try
        {
            var xml = TaskSchedulerService.OpenRootFolder().GetTask(TaskName).GetXml();
            return xml.Contains(SecurityElement.Escape(ExePath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or COMException)
        {
            return false;
        }
    }

    public static void Enable()
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>登录时启动散热模式切换托盘程序</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{SecurityElement.Escape(ExePath)}"</Command>
                </Exec>
              </Actions>
            </Task>
            """;

        TaskSchedulerService.OpenRootFolder().RegisterTask(
            TaskName, xml, TaskCreation.CreateOrUpdate, default, default, TaskLogonType.InteractiveToken, default);
    }

    public static void Disable()
    {
        try
        {
            TaskSchedulerService.OpenRootFolder().DeleteTask(TaskName, 0);
        }
        catch (FileNotFoundException)
        {
            // 任务不存在（HRESULT 0x80070002 会被转换成 FileNotFoundException）
        }
    }
}
