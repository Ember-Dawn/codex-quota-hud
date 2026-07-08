# CodexQuotaHud MVP 修复指令：额度读取 + 进度条 UI

> 目标仓库：`E:\github\codex-quota-hud`  
> 技术栈：C# + WinForms + .NET 8/9 SDK 均可  
> 执行对象：Codex  
> 任务类型：小步修复，不要重构成大项目

---

## 0. 本次任务的总目标

请在当前 WinForms MVP 项目里完成两件事：

1. **先修额度读取**
   - 当前项目默认执行 `codex quota`，这大概率不是正确读取方式。
   - 请改为模仿原 Electron 项目的思路：启动 `codex app-server --listen stdio://`，通过 stdin/stdout 发送 JSON 请求，读取 `account/rateLimits/read` 返回的额度数据。

2. **再把 UI 改成轻量进度条**
   - 未吸附：显示完整信息，包括 7d / 5h 进度条、百分比、reset 时间、updated/错误信息。
   - 顶部吸附折叠态：只显示 7d / 5h 两条进度条，不显示百分比，不显示 reset 时间。
   - 吸附后鼠标悬停展开：复用未吸附的完整布局。

本次不要做节奏判断、历史记录、设置窗口、开机自启、自动更新、主题系统、复杂动画。

---

## 1. 节省 token 的硬性规则

请遵守：

1. 不要读取或引用旧 Electron 仓库的全部代码。
2. 不要递归读取整个项目。
3. 不要生成大量说明文字。
4. 每次只打开必要文件。
5. 优先修改这些文件：
   - `CodexQuotaReader.cs`
   - `QuotaParser.cs`
   - `QuotaModels.cs`
   - `MainHudForm.cs`
   - 必要时新增 `QuotaBarControl.cs`
   - 必要时小改 `README.md`
6. 不要引入第三方 NuGet UI 库。
7. 不要新增复杂目录结构。
8. 不要新增测试框架。
9. 修改完成后只运行：
   - `dotnet build`
   - 必要时 `dotnet run` 手动验证

---

## 2. 当前问题

当前 HUD 显示类似：

```text
7d: --
5h: --
读取失败: Quota
```

原因很可能是当前 MVP 默认执行：

```powershell
codex quota
```

但 Codex CLI 可能没有这个命令。

请把读取方式改成 Codex app-server 协议。

---

## 3. 第一步：修额度读取

### 3.1 不再依赖 `codex quota`

请修改 `CodexQuotaReader.cs`：

- 不要再默认运行 `codex quota`。
- 可以保留 `CODEX_QUOTA_COMMAND` 作为调试兜底，但默认路径应使用下面的 app-server 方案。
- 如果实现兜底过于复杂，可以本次直接删除 `CODEX_QUOTA_COMMAND` 逻辑，保持代码简单。

---

### 3.2 Codex 可执行文件查找顺序

请实现 `ResolveCodexPath()`，按以下顺序查找：

1. 环境变量：

```text
CODEX_CLI_PATH
```

如果存在且文件存在，使用它。

2. 固定路径：

```text
%LOCALAPPDATA%\OpenAI\Codex\bin\codex.exe
```

如果存在，使用它。

3. 版本化目录：

```text
%LOCALAPPDATA%\OpenAI\Codex\bin\*\codex.exe
```

查找所有子目录里的 `codex.exe`，选择最近修改时间最新的一个。

4. 最后回退：

```text
codex
```

这一步让系统从 PATH 里查找。

---

### 3.3 启动 Codex app-server

用 C# `ProcessStartInfo` 启动：

```text
codex app-server --listen stdio://
```

要求：

- `UseShellExecute = false`
- `RedirectStandardInput = true`
- `RedirectStandardOutput = true`
- `RedirectStandardError = true`
- `CreateNoWindow = true`

---

### 3.4 发送 JSON 请求

app-server 是一行一个 JSON 的 stdio 协议。

请先发送 initialize：

```json
{"id":1,"method":"initialize","params":{"clientInfo":{"name":"codex-quota-hud","title":"Codex Quota HUD","version":"0.1.0"},"capabilities":null}}
```

然后发送读取额度：

```json
{"id":2,"method":"account/rateLimits/read"}
```

每条 JSON 后面必须写入换行，并 flush。

---

### 3.5 读取 JSON 响应

请逐行读取 stdout。

注意：

- 不是每一行都一定有 `id`。
- 如果 JSON 没有 `id`，忽略。
- 找到 `id == 1` 的响应后，继续等待 `id == 2`。
- 找到 `id == 2` 的响应后，读取其中的 `result`。
- 如果返回里有 `error`，显示错误信息。
- 超时时间建议 12 秒。
- 读取完成后结束 child process，避免残留进程。

---

### 3.6 目标 JSON 结构

读取结果里通常要找：

```text
result.rateLimitsByLimitId.codex
```

其中可能包含：

```text
primary
secondary
```

每个窗口里可能有：

```text
usedPercent
windowDurationMins
resetsAt
```

请把它标准化成当前项目里的模型。

---

### 3.7 7d / 5h 的识别

不要盲目假设 primary 一定是 7d 或 5h。

请优先根据 `windowDurationMins` 判断：

```text
约 10080 分钟 = 7d
约 300 分钟 = 5h
```

建议：

- `windowDurationMins >= 9000` 视为 7d。
- `windowDurationMins >= 200 && windowDurationMins <= 600` 视为 5h。

如果缺少 `windowDurationMins`：

- 可以临时 fallback 到 `primary` / `secondary`。
- 但请在代码注释里写明这是 fallback。

---

### 3.8 百分比计算

Codex 返回的是 `usedPercent`。

HUD 要显示剩余额度，所以：

```text
remainingPercent = 100 - usedPercent
```

要求：

- 四舍五入到整数。
- clamp 到 0 到 100。
- 如果缺少或无法解析，显示 `--`，不要崩溃。

---

### 3.9 reset 时间转换

Codex 返回的 `resetsAt` 是 Unix 秒时间戳。

请转换为本地时间显示。

建议格式：

```text
MM-dd HH:mm
```

对于 5h 窗口，也可以显示：

```text
HH:mm
```

为了代码简单，本次统一用：

```text
MM-dd HH:mm
```

---

### 3.10 错误信息

如果读取失败，HUD 不要崩溃。

请显示简短错误，例如：

```text
读取失败: codex not found
读取失败: app-server timeout
读取失败: missing rateLimitsByLimitId.codex
```

为了调试，请在项目目录或用户数据目录写一个轻量日志：

```text
debug.log
```

日志里记录：

- 实际使用的 codex 路径
- 启动参数
- stderr
- 关键异常 message
- 必要时记录 id=2 响应的 JSON 前 2000 字符

不要把日志系统做复杂，简单 append 文本即可。

---

## 4. 第二步：UI 改成进度条

### 4.1 不要使用 WinForms 默认 ProgressBar

请新增一个轻量自绘控件：

```text
QuotaBarControl.cs
```

继承：

```csharp
Control
```

使用 `OnPaint` 自己画进度条。

原因：

- WinForms 默认 ProgressBar 改颜色麻烦。
- 自绘控件更适合 HUD。
- 折叠态不显示百分比更容易控制。

---

### 4.2 `QuotaBarControl` 最少属性

请实现这些属性：

```csharp
public string Title { get; set; }
public double? Percent { get; set; }
public bool ShowPercentText { get; set; }
public Color FillColor { get; set; }
public Color TrackColor { get; set; }
public Color TextColor { get; set; }
```

行为：

- `Percent == null` 时画空条，并显示 `--` 或仅显示标题。
- `ShowPercentText == true` 时显示 `72%`。
- `ShowPercentText == false` 时不显示百分比。
- 进度条宽度根据 `Percent` 计算。
- 请开启双缓冲，减少闪烁。

---

### 4.3 推荐颜色

先写死颜色，不做设置。

```text
窗口背景: #181C22
轨道背景: #2A2F36
文字颜色: #F2F4F8
7d 进度条: #4EA1FF
5h 进度条: #FFB454
错误文字: #FFB454
```

C# 可用：

```csharp
ColorTranslator.FromHtml("#4EA1FF")
```

---

### 4.4 未吸附完整态

未吸附时显示完整 HUD。

建议布局：

```text
Codex Quota

7d  [progress bar] 72%
reset: 07-10 12:00

5h  [progress bar] 46%
reset: 07-08 18:00

updated: 09:34
```

如果读取失败：

```text
updated: 09:34  读取失败: ...
```

注意：

- 不需要节奏建议。
- 不需要历史曲线。
- 不需要理想刻度线。
- 不需要近速需留线。
- 不需要按钮组。
- 不需要设置入口。

---

### 4.5 顶部吸附折叠态

顶部吸附后折叠显示：

```text
7d [progress bar]    5h [progress bar]
```

要求：

- 不显示百分比。
- 不显示 reset。
- 不显示 updated。
- 不显示标题。
- 高度尽量低，例如 24 到 32 像素。
- 宽度可以固定，例如 320 到 420 像素。
- 两条 bar 横向排列。

---

### 4.6 吸附悬停展开态

当窗口处于吸附状态，鼠标进入窗口：

```text
折叠态 -> 完整态
```

鼠标离开后：

```text
完整态 -> 折叠态
```

悬停展开时复用未吸附完整态布局，不要写第三套 UI。

也就是说，UI 状态只有：

```text
DetailView
CollapsedDockView
```

其中：

```text
未吸附 = DetailView
吸附且鼠标悬停 = DetailView
吸附且鼠标未悬停 = CollapsedDockView
```

---

## 5. 保持已有功能

请保持当前 MVP 已有功能：

- 无边框窗口
- 可拖动
- 顶部吸附
- 托盘图标
- 托盘菜单
- 手动刷新
- 退出
- `dotnet build` 通过

不要破坏这些。

---

## 6. 推荐文件职责

### `QuotaModels.cs`

只放模型，例如：

```csharp
public sealed class QuotaSnapshot
{
    public QuotaWindow? SevenDay { get; set; }
    public QuotaWindow? FiveHour { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class QuotaWindow
{
    public double? RemainingPercent { get; set; }
    public DateTime? ResetAt { get; set; }
    public int? WindowDurationMins { get; set; }
}
```

可以根据现有代码微调，不必完全照抄。

### `CodexQuotaReader.cs`

负责：

- 查找 codex 路径
- 启动 app-server
- 发送 JSON 请求
- 读取 JSON 响应
- 生成 `QuotaSnapshot`

### `QuotaParser.cs`

如果还需要，可以只负责从 JSON element 中提取窗口。

不要再以解析 `codex quota` 纯文本为主要路径。

### `MainHudForm.cs`

负责：

- 布局
- 切换 DetailView / CollapsedDockView
- 定时刷新
- 手动刷新
- 托盘菜单

### `QuotaBarControl.cs`

只负责画进度条。

---

## 7. 验收标准

完成后请运行：

```powershell
dotnet build
```

必须通过。

然后可以运行：

```powershell
dotnet run
```

人工检查：

1. HUD 能启动。
2. 不再显示 `读取失败: Quota` 这种来自 `codex quota` 的错误。
3. 如果 Codex 已安装并登录，能显示 7d / 5h 剩余进度。
4. 如果读取失败，能显示简短错误，且 `debug.log` 有可用信息。
5. 未吸附时显示完整信息。
6. 吸附后只显示两条进度条，不显示百分比。
7. 鼠标悬停吸附条时展开为完整信息。
8. 鼠标离开后折叠。
9. 托盘菜单仍可退出。
10. `dotnet build` 通过。

---

## 8. 本次不要做

请明确不要做：

- 不做节奏判断。
- 不做历史记录。
- 不做理想剩余线。
- 不做近速需留线。
- 不做灯效。
- 不做设置窗口。
- 不做开机自启。
- 不做自动更新。
- 不做 GitHub Actions。
- 不做安装包。
- 不做单文件发布。
- 不做多语言。
- 不做复杂主题系统。
- 不做四边吸附。
- 不做鼠标穿透。

---

## 9. 如果遇到 app-server 协议不兼容

如果 `account/rateLimits/read` 返回结构和预期不同：

1. 不要大改 UI。
2. 把 id=2 的响应 JSON 前 2000 字符写到 `debug.log`。
3. HUD 显示：

```text
读取失败: unknown rate limit response
```

4. 保持程序不崩溃。
5. 保持 `dotnet build` 通过。

---

## 10. 完成后的简短报告格式

完成后只需要报告：

```text
完成：
- 修复额度读取，改用 codex app-server stdio 协议
- 新增/更新进度条 UI
- 顶部吸附折叠态不显示百分比
- dotnet build 通过

如果失败：
- 失败位置
- debug.log 里最关键的错误
```

不要输出超长说明。
