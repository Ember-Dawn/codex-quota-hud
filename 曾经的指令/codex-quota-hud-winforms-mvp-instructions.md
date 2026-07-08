# Codex 指令：从零实现 Codex Quota HUD WinForms MVP

## 0. 工作目录

请在以下目录中工作：

```powershell
E:\github\codex-quota-hud
```

这是一个新建空项目目录。目标是创建一个最小可行版本，不要迁移旧 Electron/Tauri 项目，不要复制旧项目代码。

---

## 1. 项目目标

实现一个 Windows 轻量桌面 HUD，用 C# + WinForms 编写，用于显示 Codex 额度。

只做 MVP：

1. 额度读取
2. 额度标准化
3. 轻量 HUD
4. 系统托盘
5. 顶部自动吸附
6. 吸附后折叠显示 7d / 5h 余量
7. 鼠标悬停在吸附条上时展开，展开界面与未吸附界面共用同一套详细布局

不要实现：

- 节奏判断
- 历史学习算法
- 复杂灯效
- 设置窗口
- 自动更新
- 开机自启
- 多主题
- WebView / Electron / Tauri
- 单元测试框架，除非非常必要
- 过度抽象架构

---

## 2. Token / 上下文节省规则

为了节省上下文，请严格遵守：

1. 不要递归读取整个目录。
2. 不要读取或分析以下目录：

```text
.git/
bin/
obj/
.vs/
.idea/
node_modules/
dist/
build/
target/
packages/
```

3. 不要创建大量文件。
4. 不要添加大段注释。
5. 不要引入第三方 UI 框架。
6. 不要搜索旧仓库，不要对比旧 Electron/Tauri 代码。
7. 每次修改前只查看必要文件。
8. 完成后只运行必要验证命令。
9. 优先使用 .NET / WinForms 标准库。
10. 如果遇到不确定的 Codex CLI 额度命令，不要大范围搜索；先运行少量本地 help 命令确认。

允许运行的本地确认命令：

```powershell
codex --version
codex --help
codex quota --help
```

如果 `codex quota` 不存在，不要卡住；实现可配置命令，默认值先写成 `codex quota`，并在 README 说明可通过环境变量覆盖。

---

## 3. 技术路线

使用：

```text
C#
WinForms
.NET 10 Windows，如果本机没有 .NET 10 SDK，则用 .NET 8 Windows
Windows x64
```

先检查 SDK：

```powershell
dotnet --list-sdks
```

如果有 .NET 10：

```powershell
dotnet new winforms -n CodexQuotaHud -o . --framework net10.0-windows
```

如果没有 .NET 10 但有 .NET 8：

```powershell
dotnet new winforms -n CodexQuotaHud -o . --framework net8.0-windows
```

如果模板命令失败，可以手动创建 `.csproj`。项目文件至少需要：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

如果使用 .NET 8，把 `TargetFramework` 改成：

```xml
<TargetFramework>net8.0-windows</TargetFramework>
```

---

## 4. 推荐最小文件结构

请保持结构简单：

```text
codex-quota-hud/
├─ CodexQuotaHud.csproj
├─ Program.cs
├─ MainHudForm.cs
├─ QuotaModels.cs
├─ CodexQuotaReader.cs
├─ QuotaParser.cs
├─ .gitignore
└─ README.md
```

暂时不要创建 `Services/`、`Forms/`、`Models/` 子目录，MVP 阶段保持扁平结构，降低上下文复杂度。

---

## 5. 核心数据模型

创建 `QuotaModels.cs`。

需要这些模型即可：

```csharp
public sealed class QuotaSnapshot
{
    public QuotaWindow SevenDay { get; set; } = new();
    public QuotaWindow FiveHour { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string RawText { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public sealed class QuotaWindow
{
    public double? RemainingPercent { get; set; }
    public DateTime? ResetAt { get; set; }

    public string PercentText => RemainingPercent.HasValue
        ? $"{RemainingPercent.Value:0.#}%"
        : "--";

    public string ResetText => ResetAt.HasValue
        ? ResetAt.Value.ToString("yyyy-MM-dd HH:mm")
        : "--";
}
```

---

## 6. 额度读取

创建 `CodexQuotaReader.cs`。

职责：只负责调用命令，返回原始文本，不解析 UI。

要求：

1. 使用 `System.Diagnostics.Process`。
2. 支持环境变量覆盖命令：

```text
CODEX_QUOTA_COMMAND
```

3. 默认命令：

```text
codex quota
```

4. 捕获 stdout 和 stderr。
5. 设置超时，例如 15 秒。
6. 不要阻塞 UI 线程，提供 async 方法。

建议接口：

```csharp
public sealed class CodexQuotaReader
{
    public Task<string> ReadRawAsync(CancellationToken cancellationToken = default);
}
```

命令解析规则：

- 如果 `CODEX_QUOTA_COMMAND` 存在，按简单命令行分割执行。
- MVP 可以只支持普通空格分割，不需要完整 shell parser。
- 如果失败，把 stderr / exit code 放进异常消息。

---

## 7. 额度标准化 / 解析

创建 `QuotaParser.cs`。

职责：把原始文本转成 `QuotaSnapshot`。

因为 Codex CLI 输出格式可能变化，MVP 采用宽松解析：

1. 先保存 `RawText`。
2. 用正则寻找 7d / 7 day / weekly 附近的百分比。
3. 用正则寻找 5h / 5 hour 附近的百分比。
4. 尝试寻找 reset / resets / reset at 附近的时间。
5. 如果解析不到，界面显示 `--`，不要崩溃。
6. 如果读取命令失败，在 `Error` 中显示错误。

建议接口：

```csharp
public sealed class QuotaParser
{
    public QuotaSnapshot Parse(string rawText);
    public QuotaSnapshot FromError(string error);
}
```

正则不需要完美，先覆盖常见格式即可，例如：

```text
7d 72%
7 day 72%
weekly 72%
5h 46%
5 hour 46%
```

不要为了解析所有未知格式写大量复杂代码。MVP 的原则是：读不到就显示 `--` 和错误/原始摘要，后续再迭代。

---

## 8. HUD 窗口设计

创建 `MainHudForm.cs`。

窗口要求：

1. 无边框。
2. TopMost。
3. 不显示任务栏。
4. 默认尺寸约：`320 x 120`。
5. 顶部吸附折叠尺寸约：`220 x 32`。
6. 背景简洁，不要复杂美术。
7. 左键拖动窗口。
8. 右键可以打开托盘同款菜单，或者至少不崩溃。

状态只保留三个：

```csharp
private bool _isDocked;
private bool _isExpanded = true;
private bool _isDragging;
```

显示逻辑：

```text
未吸附：展开视图
吸附 + 鼠标未悬停：折叠视图，只显示 7d / 5h
吸附 + 鼠标悬停：展开视图，和未吸附时完全同一套详细布局
```

也就是说只需要两个布局：

```text
DetailView
MiniView
```

不要分别实现“未吸附详细布局”和“吸附展开详细布局”。它们必须复用同一个 DetailView。

---

## 9. 自动吸附规则

MVP 只做顶部吸附，不做四边吸附。

规则：

1. 用户拖动窗口。
2. 鼠标松开时，如果窗口顶部距离当前屏幕工作区顶部小于等于 16px，则吸附到顶部。
3. 吸附后窗口 Y = 当前屏幕工作区 Top。
4. 吸附后切换到折叠视图。
5. 鼠标进入吸附窗口时展开。
6. 鼠标离开吸附窗口时折叠。
7. 从吸附状态再次拖动时，先解除吸附，再进入拖动。

使用：

```csharp
Screen.FromControl(this).WorkingArea
```

不要做复杂多显示器逻辑，先基于当前窗口所在屏幕即可。

---

## 10. 托盘功能

在 `MainHudForm` 中直接实现 `NotifyIcon`，MVP 不单独拆 `TrayService`。

托盘菜单只需要：

```text
显示/隐藏
刷新
退出
```

行为：

1. 单击或双击托盘图标：显示/隐藏窗口。
2. 点击刷新：立即读取额度。
3. 点击退出：释放托盘图标并退出程序。
4. 用户点窗口关闭按钮时，如果无边框没有关闭按钮，可以暂时不处理；如果实现关闭，应该隐藏到托盘而不是退出。

如果没有自定义 icon，可以临时使用系统图标：

```csharp
SystemIcons.Application
```

---

## 11. 刷新逻辑

在 `MainHudForm` 中使用一个 `System.Windows.Forms.Timer`。

MVP 刷新策略：

```text
启动后立即刷新一次
之后每 60 秒刷新一次
托盘菜单可以手动刷新
```

刷新时：

1. 显示 `更新中...`。
2. 调用 `CodexQuotaReader.ReadRawAsync()`。
3. 调用 `QuotaParser.Parse()`。
4. 更新 UI。
5. 如果失败，调用 `QuotaParser.FromError()`，UI 显示 `读取失败`，但程序不能崩溃。

不要实现历史文件。
不要实现 settings.json。
不要实现刷新间隔设置。

---

## 12. UI 内容

DetailView 至少显示：

```text
Codex Quota
7d: 72%    reset: 2026-07-10 12:00
5h: 46%    reset: 2026-07-08 18:00
updated: 14:30
```

如果读取失败：

```text
Codex Quota
7d: --
5h: --
读取失败：<简短错误>
```

MiniView 只显示：

```text
7d 72%   5h 46%
```

不要做复杂进度条。MVP 先用 Label。后续再加 ProgressBar。

---

## 13. GitHub 项目文件

创建 `.gitignore`，至少包含：

```gitignore
bin/
obj/
.vs/
.idea/
*.user
*.suo
*.rsuser
*.log
.DS_Store
```

创建 `README.md`，内容保持简洁，包含：

1. 项目名称：Codex Quota HUD
2. 一句话说明：A lightweight Windows HUD for Codex quota.
3. 当前状态：MVP / experimental
4. 功能列表：额度读取、标准化、HUD、托盘、顶部吸附、悬停展开
5. 运行命令：

```powershell
dotnet run
```

6. 发布命令：

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

7. 环境变量说明：

```powershell
$env:CODEX_QUOTA_COMMAND = "codex quota"
```

8. 暂不包含：节奏判断、历史分析、设置窗口。

不要添加 LICENSE，除非用户以后明确要求。

---

## 14. 验证命令

完成后运行：

```powershell
dotnet build
```

如果通过，再运行：

```powershell
dotnet run
```

如果当前环境无法打开 GUI，至少保证：

```powershell
dotnet build
```

通过。

不要做 release publish，除非 build 已经通过并且没有明显问题。

---

## 15. 完成时请汇报

完成后请只汇报以下内容：

1. 创建了哪些文件。
2. 使用的是 `net10.0-windows` 还是 `net8.0-windows`。
3. `dotnet build` 是否通过。
4. 如何运行。
5. 已知限制。

不要输出大段源码。
不要输出完整 diff。
不要输出无关解释。

---

## 16. MVP 成功标准

只要满足下面标准，就算本阶段完成：

1. 项目可以 `dotnet build`。
2. 可以启动一个无边框置顶小窗口。
3. 窗口显示 7d / 5h 文本。
4. 能通过托盘显示/隐藏/刷新/退出。
5. 拖到屏幕顶部附近可以吸附成细条。
6. 鼠标悬停吸附条可以展开。
7. 鼠标离开后可以折叠。
8. 调用 Codex CLI 失败时程序不崩溃。

达到这些就停止，不要继续扩展。
