using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace LegionFanModeTool.Interop;

// 任务计划程序 2.0 的 COM 接口（taskschd.h），用 COM 源代码生成器声明，对裁剪安全。
// COM 按虚函数表的位置调用方法，所以顺序必须与头文件一致；用不到的方法只占位，签名无关紧要。

[GeneratedComInterface]
[Guid("00020400-0000-0000-C000-000000000046")]
internal partial interface IDispatch
{
    void GetTypeInfoCount();
    void GetTypeInfo();
    void GetIDsOfNames();
    void Invoke();
}

[GeneratedComInterface]
[Guid("2FABA4C7-4DA9-4013-9697-20CC3FD40F85")]
internal partial interface ITaskService : IDispatch
{
    ITaskFolder GetFolder([MarshalAs(UnmanagedType.BStr)] string path);
    void GetRunningTasks();
    void NewTask();
    void Connect(EmptyVariant serverName, EmptyVariant user, EmptyVariant domain, EmptyVariant password);
}

[GeneratedComInterface]
[Guid("8CFAC062-A080-4C15-9A88-AA7C2AF80DFC")]
internal partial interface ITaskFolder : IDispatch
{
    void GetName();
    void GetPath();
    void GetFolder();
    void GetFolders();
    void CreateFolder();
    void DeleteFolder();
    IRegisteredTask GetTask([MarshalAs(UnmanagedType.BStr)] string path);
    void GetTasks();
    void DeleteTask([MarshalAs(UnmanagedType.BStr)] string name, int flags);
    IRegisteredTask RegisterTask(
        [MarshalAs(UnmanagedType.BStr)] string path,
        [MarshalAs(UnmanagedType.BStr)] string xml,
        TaskCreation flags,
        EmptyVariant userId,
        EmptyVariant password,
        TaskLogonType logonType,
        EmptyVariant sddl);
}

[GeneratedComInterface]
[Guid("9C86F320-DEE3-4DD1-B972-A303F26B061E")]
internal partial interface IRegisteredTask : IDispatch
{
    void GetName();
    void GetPath();
    void GetState();
    void GetEnabled();
    void SetEnabled();
    void Run();
    void RunEx();
    void GetInstances();
    void GetLastRunTime();
    void GetLastTaskResult();
    void GetNumberOfMissedRuns();
    void GetNextRunTime();
    void GetDefinition();

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetXml();
}

internal enum TaskCreation
{
    CreateOrUpdate = 6,
}

internal enum TaskLogonType
{
    InteractiveToken = 3,
}

/// <summary>VT_EMPTY 的 VARIANT（24 字节），用于"使用默认值"的可选参数。</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct EmptyVariant
{
    private readonly ushort _type;
    private readonly ushort _reserved1, _reserved2, _reserved3;
    private readonly nint _value1, _value2;
}

internal static partial class TaskSchedulerService
{
    private static readonly Guid ClsidTaskScheduler = new("0F87369F-A4E5-4CFC-BD3E-73E6154572DD");
    private const uint ClsctxInprocServer = 1;

    /// <summary>连接本机任务计划程序，返回根目录。</summary>
    public static ITaskFolder OpenRootFolder()
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(ClsidTaskScheduler, 0, ClsctxInprocServer,
            typeof(ITaskService).GUID, out var service));
        service.Connect(default, default, default, default);
        return service.GetFolder(@"\");
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out ITaskService service);
}
