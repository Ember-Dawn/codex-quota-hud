# Antigravity 新版额度读取集成说明（App Local 与 AGY CLI）

> 适用场景：给 Windows 桌面 HUD、状态栏工具、托盘工具等集成 Antigravity 新版 Gemini 额度显示。  
> 目标项目示例：`codex-quota-hud`。  
> 整理时间：2026-07-08。  
> 测试环境：Windows，Antigravity App v2.2.1，Antigravity CLI `agy`。  
> 重要声明：本文记录的是**非官方本地接口**与实测行为。接口、进程参数、字段名、刷新机制都可能随 Antigravity 更新而变化。

---

## 1. 文档目的

本文整理两种已经实测可用的 Antigravity 新版额度读取方式：

1. **App Local Provider**：读取正在运行的 Antigravity App 本地 `language_server.exe`。
2. **AGY CLI Provider**：读取正在运行的 Antigravity CLI `agy.exe` 本地 quota server。

本文不替产品选择默认方案。两种方式都是 available，开发者可以按用户习惯、产品定位、资源管理策略自行选择。

---

## 2. 新版额度模型

Antigravity 新版额度不应再按 `Gemini Flash`、`Gemini Pro` 分别展示，而应按 quota group + bucket 展示。

典型结构：

```text
Gemini Models
  - Weekly Limit
  - Five Hour Limit

Claude and GPT models
  - Weekly Limit
  - Five Hour Limit
```

只显示 Gemini 时，核心字段是：

```text
Gemini Models
  gemini-weekly.remainingFraction
  gemini-weekly.resetTime
  gemini-5h.remainingFraction
  gemini-5h.resetTime
```

建议 UI：

```text
Antigravity · Gemini
Weekly  95.62%
5h      95.23%

Checked 18:19:13
Changed 18:18:12
Source  App Local / AGY CLI
```

不建议 UI：

```text
Gemini Flash: 95.23%
Gemini Pro:   95.23%
```

原因：Flash / Pro 属于同一个 Gemini quota group。

---

## 3. 官方刷新间隔与公开信息

### 3.1 官方未公开本地 quota endpoint 的固定刷新间隔

截至本文整理时，未找到 Google Antigravity 官方文档明确说明以下内容：

```text
RetrieveUserQuotaSummary 每隔多少秒刷新一次
App local language_server 每隔多少秒同步一次 quota
AGY CLI local server 每隔多少秒同步一次 quota
```

官方文档可以确认的相关信息包括：

- Antigravity CLI 与订阅集成，可用于监控与管理 AI Premium credits 和 usage quotas。
- Antigravity 计划中存在 5-hour 与 weekly 额度概念。
- Antigravity CLI 是轻量 TUI，具有会话历史、工具调用、子代理等能力。
- Windows 安装的 `agy.exe` 位于用户目录下，例如 `C:\Users\<Username>\AppData\Local\agy\bin`。
- CLI 自更新机制有自己的 TTL/debounce 逻辑，但这不是 quota 刷新间隔。

因此，产品中不要写死“官方每 N 秒刷新额度”。更稳妥的描述是：

```text
本工具每 30-60 秒检查一次本地 Antigravity quota snapshot；
额度值是否变化取决于 Antigravity 本地状态同步节奏。
```

### 3.2 实测观察到的刷新行为

App Local 实测：

```text
轮询间隔：30 秒
Endpoint：pid=6280, kind=hub, port=6662, version=2.2.1

18:07:31 - 18:12:37
  gemini-weekly = 95.98%
  gemini-5h     = 98.40%

18:13:07
  gemini-weekly = 95.71%  Changed=True
  gemini-5h     = 95.82%  Changed=True

18:18:12
  gemini-weekly = 95.62%  Changed=True
  gemini-5h     = 95.23%  Changed=True
```

结论：App Local 在**同一个 PID / 端口 / CSRF** 下可以自然刷新，不需要重启 Antigravity。

AGY CLI 实测：

```text
轮询间隔：30 秒
Endpoint：pid=54808, port=12724

18:40:19 - 18:45:19
  gemini-weekly = 96.5760%
  gemini-5h     = 95.6181%

18:45:49
  gemini-weekly = 95.5951%  CHANGED
  gemini-5h     = 91.2161%  CHANGED
```

结论：AGY CLI 在**同一个 CLI 进程 / 端口**下也可以自然刷新，不需要重启 `agy`。

### 3.3 HUD 刷新建议

不管使用哪种 provider，建议记录两个时间：

| 字段 | 含义 |
|---|---|
| `CheckedAt` | 最近一次成功调用本地接口的时间 |
| `ChangedAt` | 额度数值最近一次发生变化的时间 |

这样用户可以理解：程序确实在检查，但 Antigravity 本地 snapshot 不一定每次都会变。

---

## 4. 方案 A：App Local Provider

### 4.1 依赖条件

需要：

```text
Antigravity App 正在运行并已登录
Antigravity App 的 language_server.exe 存在
```

不需要：

```text
OAuth
agy CLI
Antigravity extension
Google Cloud API direct integration
```

### 4.2 工作方式

```text
发现 Antigravity App language_server.exe
  -> 从进程命令行提取 csrf_token
  -> 枚举该进程监听端口
  -> 用 GetUnleashData probe 可用 HTTPS 端口
  -> 调 RetrieveUserQuotaSummary
  -> 解析 Gemini Models / gemini-weekly / gemini-5h
```

### 4.3 进程发现

Windows 中搜索：

```text
language_server.exe
```

筛选 command line 包含：

```text
antigravity
```

优先选择包含这些参数的进程：

```text
--subclient_type hub
--app_data_dir antigravity
--override_ide_name antigravity
```

实测命令行示例：

```text
C:\Users\Cyan\AppData\Local\Programs\Antigravity\resources\bin\language_server.exe
  --standalone
  --override_ide_name antigravity
  --subclient_type hub
  --override_ide_version 2.2.1
  --override_user_agent_name antigravity
  --https_server_port 0
  --csrf_token <csrf>
  --app_data_dir antigravity
  --api_server_url https://generativelanguage.googleapis.com
  --cloud_code_endpoint https://daily-cloudcode-pa.googleapis.com
  --enable_sidecars
```

注意：CSRF token 只用于本地请求，日志中必须打码。

### 4.4 端口发现

不要假设固定端口。

使用该 PID 的监听端口：

```powershell
Get-NetTCPConnection -OwningProcess $processId -State Listen |
  Select-Object -ExpandProperty LocalPort -Unique
```

对每个端口请求：

```text
POST https://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/GetUnleashData
```

成功的端口就是可用 connect port。

### 4.5 请求头

```http
Content-Type: application/json
Connect-Protocol-Version: 1
X-Codeium-Csrf-Token: <csrf_token>
```

### 4.6 请求 body

```json
{
  "metadata": {
    "ideName": "antigravity",
    "extensionName": "antigravity",
    "locale": "en",
    "ideVersion": "2.2.1"
  }
}
```

`ideVersion` 可以从 `--override_ide_version` 提取。没有时可用 `unknown`。

### 4.7 主 endpoint

```text
POST https://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary
```

### 4.8 返回结构示例

```json
{
  "response": {
    "groups": [
      {
        "displayName": "Gemini Models",
        "description": "Models within this group: Gemini Flash, Gemini Pro",
        "buckets": [
          {
            "bucketId": "gemini-weekly",
            "displayName": "Weekly Limit",
            "window": "weekly",
            "remainingFraction": 0.9562,
            "resetTime": "2026-07-15T16:45:29Z"
          },
          {
            "bucketId": "gemini-5h",
            "displayName": "Five Hour Limit",
            "window": "5h",
            "remainingFraction": 0.9523,
            "resetTime": "2026-07-09T02:45:29Z"
          }
        ]
      }
    ]
  }
}
```

注意：某些字段可能缺失，例如 `description`、`window`、`resetTime`。解析器必须宽松。

### 4.9 App Local 优缺点

| 维度 | 优点 | 缺点 / 限制 |
|---|---|---|
| 用户依赖 | 用户只需打开 Antigravity App | App 关闭时读不到 |
| 额外安装 | 不需要安装 CLI | 依赖 App 内部本地接口 |
| UI 干扰 | 不需要额外终端 | App 必须处于运行状态 |
| 刷新能力 | 实测同 PID 下可自然刷新 | 不保证每次轮询都有变化 |
| 稳定性 | 与用户实际使用的 App 额度视图贴近 | 非官方接口，可能随版本变化 |
| 安全边界 | 只读本地接口 | 需要处理本地 CSRF token，日志必须打码 |

---

## 5. 方案 B：AGY CLI Provider

### 5.1 依赖条件

需要：

```text
Antigravity CLI 已安装
agy.exe 进程正在运行
用户已经完成 CLI 登录 / trust folder 等首次交互
```

不需要：

```text
Antigravity App 正在运行
OAuth direct integration
Antigravity extension
```

### 5.2 工作方式

```text
发现 agy.exe 进程
  -> 枚举该进程监听端口
  -> 无 CSRF 请求 RetrieveUserQuotaSummary
  -> 找到返回 Gemini Models 的端口
  -> 轮询 gemini-weekly / gemini-5h
```

AGY CLI endpoint 与 App Local endpoint 的主要差异：

| 项目 | App Local | AGY CLI |
|---|---|---|
| 进程 | `language_server.exe` | `agy.exe` |
| CSRF | 需要 `X-Codeium-Csrf-Token` | 实测不需要 |
| 依赖 UI | Antigravity App | AGY CLI TUI / 进程 |
| 端口 | 动态 | 动态 |
| endpoint | `RetrieveUserQuotaSummary` | `RetrieveUserQuotaSummary` |

### 5.3 安装位置

Windows 官方安装路径通常是：

```text
C:\Users\<Username>\AppData\Local\agy\bin\agy.exe
```

安装命令：

```powershell
irm https://antigravity.google/cli/install.ps1 | iex
```

### 5.4 启动方式

手动启动：

```powershell
agy
```

首次启动可能出现：

```text
登录提示
trust folder 提示
权限/配置提示
```

这些交互完成后，保持 `agy` 进程运行，HUD 才能连接它的本地 quota server。

### 5.5 请求头

```http
Content-Type: application/json
Connect-Protocol-Version: 1
```

不需要：

```http
X-Codeium-Csrf-Token
```

### 5.6 主 endpoint

```text
POST https://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary
```

返回结构与 App Local 类似，包含：

```text
Gemini Models
  gemini-weekly
  gemini-5h

Claude and GPT models
  3p-weekly
  3p-5h
```

### 5.7 AGY CLI 优缺点

| 维度 | 优点 | 缺点 / 限制 |
|---|---|---|
| 是否依赖 App | 不需要打开 Antigravity App | 需要 `agy.exe` 运行 |
| 是否能新版 quota | 实测能读 `gemini-weekly` / `gemini-5h` | 仍是非官方本地接口 |
| 刷新能力 | 实测同一 `agy` PID/端口下可自然刷新 | 不保证每次轮询都有变化 |
| 用户体验 | 可服务于不打开 App 的用户 | 可能需要 CLI 窗口、登录、trust folder |
| 资源占用 | 常驻一个 CLI 进程，一般较轻 | 比完全不运行 CLI 占用更多资源 |
| 生命周期 | 可 attach 用户已有 agy，也可由 HUD 管理 | 必须避免误杀用户自己的 agy |
| 上下文风险 | 只查 quota 不会发送 prompt，不会增加对话上下文 | 如果用户在同一 agy 里聊天/执行任务，则那是用户自己的 CLI 会话 |

---

## 6. Codex 当前模式与 AGY CLI 生命周期差异

`codex-quota-hud` 当前查询 Codex 额度的方式是 one-shot 子进程模式：

```text
每次刷新
  -> 启动 codex app-server --listen stdio://
  -> 通过 stdin/stdout 发 initialize
  -> 调 account/rateLimits/read
  -> 解析额度
  -> kill 子进程
```

这个做法不是“打开一个可见终端”，而是启动一个隐藏子进程。通常使用：

```text
UseShellExecute = false
RedirectStandardInput = true
RedirectStandardOutput = true
RedirectStandardError = true
CreateNoWindow = true
```

读完后销毁进程，因此长期占用低，但每次刷新都有冷启动开销。

AGY CLI 与 Codex 不完全相同：

| 项目 | Codex 当前模式 | AGY CLI Provider |
|---|---|---|
| 服务模式 | 明确的 `app-server --listen stdio://` | 运行中的 TUI/CLI 暴露本地 HTTPS server |
| 通信方式 | stdio JSON-RPC | localhost HTTPS Connect/JSON |
| 生命周期 | 适合 one-shot | 更适合常驻 / attach |
| UI 交互 | 可隐藏、非交互 | 首次可能有 TUI、登录、trust folder |
| 查询后销毁 | 当前实现是读完 kill | 不建议默认每次查询后 kill |

---

## 7. AGY CLI 是否必须常驻？

如果选择 AGY CLI Provider，通常需要有一个 `agy.exe` 进程保持运行，因为本地 quota endpoint 依附于该进程。

```text
agy.exe running
  -> localhost quota endpoint alive
  -> HUD can poll RetrieveUserQuotaSummary

agy.exe exited
  -> localhost quota endpoint gone
  -> HUD cannot poll AGY quota
```

### 7.1 常驻是否消耗资源？

会有资源占用，主要包括：

```text
一个 agy.exe 进程
一个本地 HTTPS 监听端口
少量内存与 idle CPU
每 30-60 秒一次本地 quota 请求
```

通常比“每次查询都冷启动 AGY”更稳定。冷启动可能涉及：

```text
初始化 CLI
加载配置
处理 workspace trust
发现端口
等待 quota server ready
```

### 7.2 CLI 是隐藏还是可见？

可有三种模式：

| 模式 | 描述 | 适用场景 |
|---|---|---|
| Attach Only | 只连接用户已经打开的 `agy.exe` | 最安全，不管理生命周期 |
| Managed AGY 可见窗口 | HUD 启动一个可见 `agy` 窗口 | 首次登录 / trust folder 时更可靠 |
| Managed AGY 隐藏启动 | HUD 后台启动 `agy.exe` | 需要实测 TUI 是否可无控制台稳定运行 |

### 7.3 反复查询是否导致上下文超限？

只查询 quota 不会导致上下文超限。

HUD 调的是：

```text
RetrieveUserQuotaSummary
```

这不是聊天 prompt，不会往 AGY 当前对话里追加消息，不会让 agent 执行任务，也不应增加上下文窗口。

会导致上下文增长的是用户在 AGY TUI 中实际输入 prompt、让 agent 读写代码或执行任务。quota polling 本身不是对话。

为了进一步隔离 Managed AGY，可将 HUD 托管的 AGY 工作目录设为专用空目录：

```text
%LOCALAPPDATA%\codex-quota-hud\agy-provider
```

这样它不会默认落在用户正在开发的 repo 中。

---

## 8. 模式 A：Attach Only AGY

Attach Only 表示 HUD 只连接已经存在的 `agy.exe`，不负责启动，也不负责关闭。

流程：

```text
发现 agy.exe
  -> 枚举监听端口
  -> 找到 RetrieveUserQuotaSummary 可用端口
  -> 轮询 quota
  -> HUD 退出时不杀 agy
```

优点：

```text
不会弹出新窗口
不会误杀用户正在使用的 CLI
生命周期简单
```

缺点：

```text
用户必须自己启动 agy
如果用户关闭 agy，HUD 只能显示 Offline / Not running
```

---

## 9. 模式 B：Managed AGY，由 HUD 启动并管理

Managed AGY 表示 HUD 可以自己启动一个 `agy.exe` 进程，并在 HUD 生命周期内复用它。

### 9.1 基本流程

```text
HUD 启动
  -> 检查是否已有可用 agy endpoint
  -> 如果用户启用 Managed AGY 且没有可用 endpoint
      -> 启动一个 HUD 托管的 agy.exe
      -> 等待本地 quota server ready
      -> 记录 PID / StartTime / WorkingDirectory / Port
  -> 每 30-60 秒轮询 RetrieveUserQuotaSummary
  -> HUD 退出时，只关闭 HUD 自己启动的 agy.exe
```

### 9.2 建议记录的信息

```text
ManagedAgyPid
ManagedAgyStartTime
ManagedAgyWorkingDirectory
ManagedAgyExecutablePath
ManagedAgyPort
ManagedAgyWasStartedByHud = true
```

### 9.3 只杀自己启动的 AGY

退出时只关闭满足这些条件的进程：

```text
PID == ManagedAgyPid
进程名仍是 agy.exe
启动时间与记录值匹配
工作目录 / 命令行符合 HUD 托管标记
```

不要执行：

```text
kill all agy.exe
```

因为这会误杀用户自己正在使用的 AGY CLI。

### 9.4 可见启动与隐藏启动

首次运行更适合可见启动：

```text
原因：可能有登录、trust folder、权限确认、TUI 初始化等交互。
```

后续如果用户已完成登录和 trust，可尝试隐藏启动：

```text
CreateNoWindow = true / WindowStyle Hidden
```

但隐藏启动不一定稳定，因为 AGY 是 TUI 程序，不是专门的 headless app-server。应提供 fallback：隐藏启动失败时提示用户手动打开 AGY。

### 9.5 Managed AGY 优缺点

| 维度 | 优点 | 缺点 / 风险 |
|---|---|---|
| 用户体验 | 可以不要求用户手动开 AGY | 首次可能仍需交互 |
| 自动化 | HUD 可自动启动和复用 provider | 生命周期管理更复杂 |
| 资源 | 常驻进程避免反复冷启动 | 多一个常驻 `agy.exe` |
| 安全 | 可用专用空目录隔离 | 必须避免误杀用户已有 agy |
| 稳定 | 同一进程可自然刷新 quota | TUI 隐藏运行能力需要实测 |

---

## 10. 能否模仿 Codex：每次查询启动 AGY，用完销毁？

理论上可以尝试：

```text
每次刷新
  -> 启动 agy.exe
  -> 等待端口 ready
  -> 调 RetrieveUserQuotaSummary
  -> kill agy.exe
```

但不建议作为默认实现，原因：

```text
AGY 是 TUI/CLI，不是专门的 stdio app-server
首次可能需要登录 / trust folder
启动到端口可用时间不稳定
隐藏启动可能不稳定
如果误匹配到用户自己的 agy，kill 会打断用户工作
频繁冷启动可能比常驻更耗资源、更慢
```

如果一定要做 one-shot AGY，必须做到：

```text
只杀自己启动的 PID
使用专用工作目录
设置启动超时
设置端口发现超时
失败时回滚为 Offline，不影响用户已有 AGY
```

---

## 11. 两种方案对比

| 维度 | App Local Provider | AGY CLI Provider |
|---|---|---|
| 需要 Antigravity App | 是 | 否 |
| 需要 AGY CLI | 否 | 是 |
| 需要 OAuth direct | 否 | 否 |
| 需要 extension | 否 | 否 |
| 是否能读新版 Gemini weekly + 5h | 是，实测通过 | 是，实测通过 |
| 是否能自然刷新 | 是，实测同 PID 下刷新 | 是，实测同 PID 下刷新 |
| 是否需要额外终端 | 否 | 可能需要，取决于 Attach/Managed/Hidden 策略 |
| 资源占用 | 使用已运行 App 的 language_server | 需要一个 `agy.exe` 进程 |
| 生命周期管理 | 只连接现有 App 进程，不应杀 App | 需要区分用户 agy 与 HUD 托管 agy |
| 风险 | App 关闭则不可用 | CLI 首次交互、trust folder、隐藏运行稳定性 |
| 适合用户 | 平时打开 Antigravity App 的用户 | 不想打开 App、愿意使用 CLI 的用户 |

---

## 12. 推荐的数据模型

```csharp
public sealed class AntigravityQuotaSnapshot
{
    public string Provider { get; init; } = "antigravity";
    public string Source { get; init; } = "app-local"; // app-local | agy-cli
    public string GroupName { get; init; } = "Gemini Models";

    public AntigravityQuotaBucket? Weekly { get; init; }
    public AntigravityQuotaBucket? FiveHour { get; init; }

    public DateTimeOffset CheckedAt { get; init; }
    public DateTimeOffset? ChangedAt { get; init; }

    public int? ProcessId { get; init; }
    public int? Port { get; init; }
    public string? ProcessKind { get; init; } // hub / app / ide / agy

    public bool IsManagedProcess { get; init; }
    public string? Error { get; init; }
}

public sealed class AntigravityQuotaBucket
{
    public string BucketId { get; init; } = "";   // gemini-weekly / gemini-5h
    public string Name { get; init; } = "";       // Weekly Limit / Five Hour Limit
    public string Window { get; init; } = "";     // weekly / 5h

    public double RemainingFraction { get; init; }
    public double RemainingPercent => RemainingFraction * 100.0;
    public double UsedPercent => 100.0 - RemainingPercent;

    public DateTimeOffset? ResetTimeUtc { get; init; }
    public string? Description { get; init; }
}
```

---

## 13. 解析策略

### 13.1 只依赖稳定字段

优先依赖：

```text
bucketId
remainingFraction
resetTime
```

不要强依赖：

```text
description
window
displayName
```

因为这些字段可能缺失或语言变化。

### 13.2 兼容两种 remainingFraction 结构

可能是：

```json
{
  "remainingFraction": 0.9523
}
```

也可能是：

```json
{
  "remaining": {
    "remainingFraction": 0.9523
  }
}
```

解析器应同时兼容。

---

## 14. 安全与日志

不要在日志中完整打印：

```text
csrf_token
完整 command line
完整 raw JSON
账号邮箱或未来可能出现的用户信息
```

可以打码：

```text
csrf=8cdea676...cb4b
endpoint=pid=6280, port=6662, source=app-local
```

AGY Provider 也不要随意 kill 所有 `agy.exe`。

---

## 15. 错误处理建议

| 场景 | 处理 |
|---|---|
| 找不到 App language_server | App Local 显示 Offline |
| 找不到 agy.exe | AGY CLI 显示 Not installed / Not running |
| endpoint 请求失败 | 重新 discovery PID / port / CSRF |
| 字段缺失 | 宽松解析，不因 `description` 缺失崩溃 |
| quota 长时间不变 | 更新 `CheckedAt`，不更新 `ChangedAt` |
| PID / port 变化 | 自动重新绑定 endpoint |
| Managed AGY 退出 | 尝试重启或显示 Offline，取决于用户设置 |

---

## 16. 最小接口摘要

### App Local

```text
Process:
  language_server.exe

Required command line args:
  --subclient_type hub
  --app_data_dir antigravity
  --csrf_token <token>

Headers:
  Content-Type: application/json
  Connect-Protocol-Version: 1
  X-Codeium-Csrf-Token: <token>

Endpoint:
  POST https://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary
```

### AGY CLI

```text
Process:
  agy.exe

Headers:
  Content-Type: application/json
  Connect-Protocol-Version: 1

Endpoint:
  POST https://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary
```

### Body

```json
{
  "metadata": {
    "ideName": "antigravity",
    "extensionName": "antigravity",
    "locale": "en",
    "ideVersion": "unknown"
  }
}
```

### Parse

```text
response.groups[]
  where displayName contains "Gemini" or bucketId starts with "gemini-"

buckets[]
  bucketId == "gemini-weekly"
  bucketId == "gemini-5h"

remainingPercent = remainingFraction * 100
usedPercent = 100 - remainingPercent
resetTime = resetTime
```

---

## 17. 官方文档参考

- Antigravity CLI Credits / Quotas: `https://antigravity.google/docs/cli-credits`
- Antigravity Plans: `https://antigravity.google/docs/plans`
- Antigravity CLI Overview: `https://antigravity.google/docs/cli-overview`
- Antigravity CLI Installation: `https://antigravity.google/docs/cli-install`
- Antigravity CLI Troubleshooting: `https://antigravity.google/docs/cli-troubleshooting`

---

## 18. 当前实测总结

```text
[App Local]
  可读取新版 Gemini weekly + 5h
  可在同一个 PID/port/CSRF 下自然刷新
  需要 Antigravity App 正在运行

[AGY CLI]
  可读取新版 Gemini weekly + 5h
  可在同一个 agy PID/port 下自然刷新
  需要 agy.exe 进程正在运行
  可 attach 用户已有进程，也可设计 Managed AGY 模式

[共同限制]
  本地接口非官方
  官方未公开固定本地刷新间隔
  每次轮询不保证数值变化
  需要 CheckedAt / ChangedAt 区分“检查时间”和“额度变化时间”
```
