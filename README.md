# LegionFanModeTool

联想拯救者散热模式切换工具。没有主界面，只有一个托盘图标。

## 使用

- **左键单击**：按 安静 → 均衡 → 野兽 循环切换（野兽模式仅在插入电源时生效）
- **右键菜单**：均衡、野兽、安静、开机自启、退出
- 托盘图标的图案表示当前模式，颜色和原生图标一样跟随任务栏的亮色/暗色主题；菜单中当前模式的图标显示强调色（野兽红、安静蓝、均衡用系统强调色，暗色主题下为白色）；用 Fn+Q 或联想电脑管家切换后会立即同步（订阅 BIOS 的模式变化事件，不轮询）
- 鼠标悬停在托盘图标上时，提示里显示两个风扇的转速和 CPU/GPU 温度（只在悬停时读取，最快每秒一次）

## 运行要求

- 支持 `LENOVO_GAMEZONE_DATA` WMI 接口的联想拯救者机型（由 BIOS 提供，不依赖联想电脑管家）。风扇转速和温度来自 `LENOVO_FAN_METHOD`，不支持时提示里只显示模式；模式变化事件订阅 `LENOVO_GAMEZONE_THERMAL_MODE_EVENT` 和 `LENOVO_GAMEZONE_SMART_FAN_MODE_EVENT`，都订阅失败时退回每 3 秒轮询
- [Windows App SDK 2.6 运行时](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)（.NET 运行时已裁剪后打包在 exe 内，无需安装）
- 管理员权限（启动时会弹出 UAC）

开机自启通过任务计划程序实现（任务名 `LegionFanModeTool`，登录时以最高权限运行）。程序本身不保存任何配置；出现未处理的错误时会记录到 `%LOCALAPPDATA%\LegionFanModeTool\crash.log`，界面层的错误记录后程序继续运行。

## 构建

```bash
dotnet publish LegionFanModeTool.csproj -c Release -o publish
```

生成单个 `publish/LegionFanModeTool.exe`（约 14 MB）。

## 技术栈

.NET 11、WinUI 3（Windows App SDK 2.6）。托盘图标直接调用 Win32 `Shell_NotifyIcon`，右键菜单是原生 WinUI `MenuFlyout`，不依赖第三方库。

`Assets/` 中的模式图标还原自联想电脑管家，版权归联想所有，仅供个人使用。
