# LegionFanModeTool

联想拯救者散热模式切换工具。没有主界面，只有一个托盘图标。

## 使用

- **左键单击托盘图标**：按 安静 → 均衡 → 野兽 循环切换（野兽模式仅在插入电源时生效）
- **右键图标打开菜单**：设置开机自启

## 运行要求

- 管理员权限（启动时会弹出 UAC）
- 支持 `LENOVO_GAMEZONE_DATA` WMI 接口的联想拯救者机型。
- 已经测试通过的机型：
  - R9000P 2021

## 构建

```bash
dotnet publish LegionFanModeTool.csproj -c Release -o publish
```

## 技术栈

- .NET 11
- WinUI 3（Windows App SDK 2.6）
