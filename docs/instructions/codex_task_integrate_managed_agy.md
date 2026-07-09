# Codex Quota HUD：集成 Managed AGY 额度显示任务说明

> 目标仓库本地路径：`E:\github\codex-quota-hud`  
> 目标平台：Windows x64  
> 技术栈：C# / WinForms / .NET 8  
> 任务类型：功能集成 + 小范围架构重构  
> 重要要求：请先阅读现有项目结构和实现，再按本文逐步修改。不要一次性大重构，不要破坏当前 Codex HUD 的吸附态、展开态、托盘菜单、手动刷新、自动刷新、设置保存等既有行为。

---

## 1. 背景

当前项目 `codex-quota-hud` 是一个 Windows 原生 HUD，用于显示 Codex 的 quota。现有 UI 有两种状态：

1. **吸附/折叠态**：贴近屏幕顶部时显示紧凑进度条。
2. **展开态**：鼠标悬停或非吸附时显示详细信息，包括标题、更新时间、`7d` / `5h` 进度条和 reset 时间。

现在需要新增 Antigravity quota 显示。Antigravity 使用 AGY CLI 方案，并且由 HUD 启动和管理一个隐藏的 `agy.exe` 进程。

新增后，UI 仍然保持原有设计思路：

```text
折叠态：紧凑、低高度、只显示 provider 名称和 7d / 5h 条
展开态：有边界感的 provider 模块卡片，显示详细信息
```

---

## 2. 总体目标

完成以下功能：

1. 设置中新增 **Enable Antigravity**，默认关闭。
2. 启用 Antigravity 后，HUD 以 **Managed AGY** 模式启动并管理一个隐藏的 `agy.exe`。
3. 使用 AGY CLI 本地 quota endpoint 读取 Antigravity Gemini quota。
4. UI 中 Codex 与 AGY 都统一显示为 `7d` / `5h`。
5. AGY 的 `gemini-weekly` 在 UI 中显示为 `7d`。
6. AGY 的 `gemini-5h` 在 UI 中显示为 `5h`。
7. 折叠态从单行 Codex 进度条升级为 provider 行：

```text
Codex   [7d] [5h]
AGY     [7d] [5h]
```

8. 展开态从单一 Codex 面板升级为多个 provider 卡片：

```text
Codex
  7d   [bar]   R Jul 10 18:39
  5h   [bar]   R        00:54
  Updated 19:54

AGY · Gemini
  7d   [bar]   R Jul 15 20:45
  5h   [bar]   R        00:54
  Source Managed AGY    Checked 19:54
```

9. 如果 AGY 读取失败，在 HUD 附近显示非模态提示，不要用阻塞式 MessageBox。
10. 将 Codex 和 AGY 的数据获取方式模块化，为未来新增 Claude、其他 CLI 或其他 provider 留出结构。

---

## 3. 非目标

本次不要做以下事情：

1. 不要实现 Antigravity App Local Provider。
2. 不要接入 OAuth、Google Cloud API 或任何远程账号 API。
3. 不要实现 usage pace、历史预测、额度建议或复杂分析。
4. 不要改变现有 Codex quota 读取协议。
5. 不要重写成 WPF、WebView、Electron 或其他 UI 框架。
6. 不要默认开启 Antigravity。
7. 不要 kill 所有 `agy.exe`。
8. 不要把完整 raw JSON、完整命令行、账号信息、token 写进日志。
9. 不要修改 GitHub Actions，除非构建确实因为文件结构变动而需要极小调整。

---

## 4. UI 统一命名规则

虽然 Antigravity 返回的是 `weekly` 和 `5h`，但 UI 层需要和 Codex 保持统一：

| Provider | 原始字段 | UI 标签 |
|---|---|---|
| Codex | 7-day quota | `7d` |
| Codex | 5-hour quota | `5h` |
| AGY | `gemini-weekly` | `7d` |
| AGY | `gemini-5h` | `5h` |

注意：实现层仍应保留原始 bucket id，例如 `gemini-weekly`、`gemini-5h`，不要在 parser 里把语义完全丢掉。只是在 UI label 中显示为 `7d` / `5h`。

---

## 5. 建议架构

请将项目从“Codex 专用 UI + Codex 专用 reader”逐步改成以下方向：

```text
src/
  Models/
    ProviderQuotaSnapshot.cs
    QuotaBucketSnapshot.cs
    QuotaProviderStatus.cs

  Providers/
    IQuotaProvider.cs

    Codex/
      CodexQuotaProvider.cs
      CodexQuotaReader.cs
      CodexQuotaParser.cs

    Antigravity/
      ManagedAgyQuotaProvider.cs
      AgyEndpointDiscovery.cs
      AntigravityQuotaParser.cs
      ManagedAgyProcess.cs

  Services/
    QuotaPoller.cs
    SettingsStore.cs
    AppSettings.cs

  UI/
    MainHudForm.cs
    ProviderCardControl.cs
    CollapsedProviderRowControl.cs
    QuotaBarControl.cs
    SettingsForm.cs
    HudToastForm.cs
```

不要求文件名完全一致，但请保持以下职责边界：

1. **Provider 层**负责读取数据。
2. **Parser 层**负责把不同接口返回值转成统一 snapshot。
3. **Poller 层**负责刷新多个 provider，避免 UI 知道每个 provider 的协议细节。
4. **UI 层**只根据统一 snapshot 渲染 provider card / collapsed row。
5. **Settings 层**负责默认值、保存、兼容旧 settings.json。

---

## 6. 统一数据模型建议

请设计一个通用 snapshot，而不是继续让 UI 直接依赖 `SevenDay` / `FiveHour` 等 Codex 专用字段。

建议字段如下：

```text
ProviderQuotaSnapshot
  ProviderId            codex / agy
  DisplayName           Codex / AGY
  Subtitle              null / Gemini
  Source                Codex CLI / Managed AGY
  Status                Disabled / Refreshing / Ok / Offline / Failed
  Buckets               list of QuotaBucketSnapshot
  UpdatedAt             最近 UI 更新或 provider 返回时间
  CheckedAt             最近成功请求接口时间
  ChangedAt             额度数值最近变化时间，可为空
  ErrorMessage          用户可读错误，可为空
  IsManagedProcess      AGY 可用
  ProcessId             AGY 可用
  Port                  AGY 可用
```

```text
QuotaBucketSnapshot
  Id                    codex-7d / codex-5h / gemini-weekly / gemini-5h
  Label                 7d / 5h
  ShortLabel            7d / 5h
  RemainingPercent      0 到 100
  ResetAt               本地时间，可为空
  RawResetTimeUtc        AGY 可用，可为空
```

UI 只关心：

```text
Provider display name
Provider subtitle
Provider status
Bucket label
Bucket remaining percent
Bucket reset time
```

---

## 7. Codex Provider 要求

Codex 当前读取方式保持不变：

```text
每次刷新
  -> 启动 codex app-server --listen stdio://
  -> initialize
  -> account/rateLimits/read
  -> 解析 7d / 5h
  -> 结束该 app-server 子进程
```

请把当前 Codex reader 包装成 `CodexQuotaProvider`，让它输出统一 `ProviderQuotaSnapshot`。

要求：

1. 不改变 Codex 协议。
2. 不改变 Codex CLI 路径发现逻辑，除非只是移动文件并保持行为一致。
3. 不让 Codex 读取失败影响 AGY 显示。
4. 不让 AGY 读取失败影响 Codex 显示。

---

## 8. AGY Provider：Managed AGY 模式

### 8.1 运行模式

Antigravity 使用 **Managed AGY**：

```text
HUD 启动一个自己管理的 agy.exe
agy.exe 隐藏运行
HUD 轮询该 agy.exe 暴露的本地 quota endpoint
HUD 退出或用户关闭 Antigravity 时，只关闭 HUD 自己启动的 agy.exe
```

不要采用以下默认模式：

```text
每次刷新都启动 agy.exe，读取一次，然后 kill
```

原因：AGY CLI 是 TUI/CLI，不是 Codex 那样明确的 stdio app-server。首次运行可能有登录、trust folder 或其他交互。频繁冷启动也更慢、更不稳定。

---

### 8.2 AGY CLI 路径发现

优先级建议：

1. 用户在设置中填写的 `AgyExecutablePath`，如果后续决定添加该设置。
2. `%LOCALAPPDATA%\agy\bin\agy.exe`
3. PATH 中的 `agy.exe`

如果找不到，AGY snapshot 状态设为 `Offline` 或 `Failed`，错误信息使用用户可读文案：

```text
AGY CLI not found. Please install Antigravity CLI and run agy once to finish setup.
```

不要崩溃，不要影响 Codex。

---

### 8.3 Managed AGY 工作目录

请使用 HUD 专用空目录作为 AGY 工作目录：

```text
%LOCALAPPDATA%\CodexQuotaHud\agy-provider
```

启动前确保目录存在。

目的：

1. 避免 AGY 默认落在用户正在开发的 repo 中。
2. 降低 trust folder 和上下文污染风险。
3. 方便识别 HUD 托管的进程。

---

### 8.4 隐藏启动要求

启动 `agy.exe` 时建议使用：

```text
UseShellExecute = false
CreateNoWindow = true
WindowStyle = Hidden
WorkingDirectory = %LOCALAPPDATA%\CodexQuotaHud\agy-provider
```

建议重定向 stdout / stderr，并异步 drain，避免子进程输出阻塞。

注意：隐藏启动不保证首次配置一定成功。如果 AGY 需要登录、trust folder 或 TUI 交互，隐藏模式可能无法完成配置。此时不要反复重启。请显示提示：

```text
AGY started but quota endpoint is not ready. Please run agy once manually to finish login/trust, then retry.
```

---

### 8.5 AGY 进程归属保护

必须记录 HUD 自己启动的进程信息：

```text
ManagedAgyPid
ManagedAgyStartTime
ManagedAgyExecutablePath
ManagedAgyWorkingDirectory
ManagedAgyPort
ManagedAgyWasStartedByHud = true
```

退出或禁用 AGY 时，只能关闭满足以下条件的进程：

```text
PID == ManagedAgyPid
进程名仍是 agy.exe
启动时间与记录值匹配，或者足够确认是同一个进程
路径是记录的 agy.exe 路径
工作目录或管理状态符合 HUD 托管信息
```

绝对不要执行：

```text
kill all agy.exe
```

如果发现用户自己打开的 `agy.exe`，不要关闭它。

---

### 8.6 AGY endpoint 发现

`agy.exe` 启动后，枚举该进程拥有的 TCP Listen 端口。

可以实现一个 Windows-only 端口发现方法，例如：

1. 调用 `netstat -ano -p tcp` 并按 PID 解析监听端口。
2. 或调用 PowerShell `Get-NetTCPConnection -OwningProcess <pid> -State Listen`。
3. 或使用其他可靠的 Windows API / helper。

不要假设固定端口。

对候选端口逐个 probe：

```text
POST https://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary
```

Headers：

```http
Content-Type: application/json
Connect-Protocol-Version: 1
```

AGY CLI 实测不需要：

```http
X-Codeium-Csrf-Token
```

Body：

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

成功条件：返回 JSON 中能找到：

```text
response.groups[].buckets[]
  bucketId == gemini-weekly
  bucketId == gemini-5h
```

---

### 8.7 HTTPS / TLS 要求

本地 endpoint 使用 HTTPS，可能是自签证书。

允许只对以下目标放宽证书验证：

```text
https://127.0.0.1:{port}/...
```

不要全局关闭 TLS 验证。不要让这个 handler 被其他远程请求复用。

---

### 8.8 请求超时与重试

建议：

```text
启动 AGY 后等待 endpoint ready：最多 10-20 秒
单次 HTTP 请求 timeout：5-10 秒
端口 probe：每个端口快速失败
刷新失败：重新 discovery 一次并 retry 一次
连续失败：标记 Offline/Failed，显示 toast，但不要频繁弹出
```

AGY 退出后，如果 Antigravity 仍启用，可按设置决定是否自动重启。初版建议：

```text
如果是 HUD 托管的 AGY 意外退出：尝试重启一次。
如果仍失败：显示 Offline，并等待下次手动 Refresh Now 或下一轮刷新。
```

---

## 9. Antigravity 返回结构与解析要求

典型返回结构：

```json
{
  "response": {
    "groups": [
      {
        "displayName": "Gemini Models",
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

解析策略：

1. 优先按 `bucketId` 查找：
   - `gemini-weekly`
   - `gemini-5h`
2. 如果 `displayName`、`description`、`window` 缺失，不要崩溃。
3. `remainingFraction` 可能直接在 bucket 上：

```json
{ "remainingFraction": 0.9523 }
```

4. 也可能在嵌套对象里：

```json
{ "remaining": { "remainingFraction": 0.9523 } }
```

5. `remainingPercent = remainingFraction * 100`
6. `resetTime` 是 UTC 字符串，UI 显示时转换成本地时间。
7. AGY UI 标签：
   - `gemini-weekly` -> `7d`
   - `gemini-5h` -> `5h`

---

## 10. 设置项要求

在 `AppSettings` / `SettingsStore` / `SettingsForm` 中新增设置。

最低要求：

```text
EnableAntigravity = false
```

建议增加：

```text
AntigravityMode = "managed-agy"
StartAgyHidden = true
CloseManagedAgyOnExit = true
AgyExecutablePath = "" 或 null
```

如果设置界面不想变复杂，初版可以只展示：

```text
[ ] Enable Antigravity
```

并在内部固定：

```text
Managed AGY
Start hidden
Close managed AGY on exit
```

设置保存要求：

1. 兼容旧 `settings.json`。
2. 旧设置文件缺少新字段时使用默认值。
3. 新字段非法时 normalize 回默认值。
4. 不要因为 settings JSON 解析失败而导致程序无法启动。

---

## 11. UI 改造要求

### 11.1 折叠态

当前折叠态只有两条 bar。改造后应按 provider 行显示。

Antigravity 未启用：

```text
Codex   [7d bar] [5h bar]
```

Antigravity 启用且读取成功：

```text
Codex   [7d bar] [5h bar]
AGY     [7d bar] [5h bar]
```

Antigravity 启用但读取失败：

```text
Codex   [7d bar] [5h bar]
AGY     Offline
```

或：

```text
AGY     [empty/disabled 7d] [empty/disabled 5h]  !
```

要求：

1. 最左边增加 provider 名称。
2. Codex 行显示 `Codex`。
3. AGY 行显示 `AGY`。
4. 条内标签仍用 `7d` / `5h`。
5. 不要在折叠态显示 reset time 或长错误信息。
6. 高度应根据启用 provider 数动态调整。
7. 保持吸附态的紧凑感。

---

### 11.2 展开态

展开态改为 provider 卡片。

Codex card：

```text
Codex                       Updated 19:54
7d   [72% bar]              R Jul 10 18:39
5h   [99% bar]              R        00:54
```

AGY card：

```text
AGY · Gemini                Checked 19:54
7d   [95% bar]              R Jul 15 20:45
5h   [91% bar]              R        00:54
Source Managed AGY
```

要求：

1. Codex 和 AGY 都有边界感，例如圆角边框或微弱分隔线。
2. 不要把 AGY 的内容硬塞到 Codex 标题下面。
3. `MainHudForm` 不应再硬编码大量 `_sevenDayLabel`、`_fiveHourLabel` 这种只适合一个 provider 的字段。
4. 如果保留部分现有控件名，也要尽量把新 UI 封装到 `ProviderCardControl` 和 `CollapsedProviderRowControl`。
5. 展开态窗口高度根据 provider 数动态调整。
6. 保留原有拖动、吸附顶部、悬停展开、右键菜单、托盘图标行为。

---

### 11.3 HUD 附近 toast

新增非模态小提示，用于 AGY 失败说明。

触发条件：

```text
EnableAntigravity == true
且 AGY provider 读取失败或未 ready
```

示例文案：

找不到 AGY：

```text
AGY CLI not found. Please install Antigravity CLI and run agy once to finish setup.
```

启动了但 endpoint 不 ready：

```text
AGY started, but quota endpoint is not ready. Please run agy once manually to finish login/trust.
```

请求失败：

```text
Failed to read AGY quota. Please make sure Antigravity CLI is installed and signed in.
```

要求：

1. 不要用 MessageBox 打断用户。
2. 提示显示在 HUD 附近。
3. 自动消失，例如 5-8 秒。
4. 同一种错误不要每轮刷新都弹；需要节流或只在错误状态变化时提示。
5. 仍然在 AGY card / row 内显示简短状态，例如 `Offline`。

---

## 12. 颜色与视觉建议

初版建议 Codex 和 AGY 共用现有 `7d` / `5h` 颜色设置：

```text
7d color -> 所有 provider 的 7d bar
5h color -> 所有 provider 的 5h bar
track color -> 所有 provider
track border color -> 所有 provider
```

不要在本次引入复杂的 per-provider color 设置，除非实现成本很低且不破坏设置界面。

---

## 13. 刷新策略

建议 `QuotaPoller` 统一刷新所有 enabled providers。

要求：

1. Codex 和 AGY 独立刷新，互不阻塞最终 UI 展示。
2. 避免同一个 provider 并发刷新。
3. `Refresh Now` 刷新所有启用 provider。
4. 自动刷新间隔沿用现有设置。
5. AGY 可加最小刷新保护，例如不要低于 30 秒。
6. `CheckedAt` 表示最近一次成功请求 endpoint。
7. `ChangedAt` 只有 quota 数值变化时更新。
8. quota 长时间不变不是错误。

---

## 14. 日志与安全要求

允许记录：

```text
provider id
status
pid
port
source
简短错误信息
```

不要记录：

```text
完整 raw JSON
完整 AGY/Codex command line
账号邮箱
token
完整 CSRF
任何未来可能出现的认证信息
```

虽然 AGY CLI 当前请求不需要 CSRF，但请为未来扩展保持安全习惯。

TLS 证书绕过只能限定在：

```text
127.0.0.1
```

不要全局关闭 TLS 验证。

---

## 15. 进程关闭策略

当用户退出 HUD 或关闭 Enable Antigravity 时：

1. 如果 AGY 进程是 HUD 启动的 managed process，则可以关闭。
2. 如果 AGY 是用户自己打开的，不要关闭。
3. 关闭前验证 PID / StartTime / Path / managed flag。
4. 不要 kill all `agy.exe`。
5. 如果关闭失败，记录简短日志，不要崩溃。

---

## 16. 推荐实施顺序

请按以下顺序提交或修改，避免大范围回归。

### 阶段 1：先抽象 UI 数据模型，不改外观行为

1. 新增统一 snapshot / bucket model。
2. 把现有 Codex reader 包装成 Codex provider。
3. MainHudForm 使用 provider snapshot 更新 UI。
4. 暂时只显示 Codex。
5. 确认现有 Codex HUD 行为不变。
6. 运行：

```powershell
dotnet build .\CodexQuotaHud.csproj
```

### 阶段 2：改造折叠态和展开态为 provider row/card

1. 新增 `ProviderCardControl`。
2. 新增 `CollapsedProviderRowControl`。
3. 折叠态显示 `Codex [7d] [5h]`。
4. 展开态显示 Codex card。
5. 保持吸附、悬停展开、拖动、托盘不变。
6. 运行 build。

### 阶段 3：新增设置 Enable Antigravity

1. AppSettings 新增字段，默认 false。
2. SettingsStore 兼容旧 JSON。
3. SettingsForm 新增 checkbox。
4. 未启用时不启动 AGY，不显示 AGY 行。
5. 运行 build。

### 阶段 4：实现 Managed AGY provider

1. AGY path discovery。
2. managed working directory。
3. hidden start。
4. owned process tracking。
5. port discovery。
6. local HTTPS request。
7. JSON parser。
8. retry / timeout / error state。
9. 运行 build。

### 阶段 5：AGY UI 和 toast

1. 启用 AGY 后显示 AGY row/card。
2. `gemini-weekly` 显示为 `7d`。
3. `gemini-5h` 显示为 `5h`。
4. 错误状态显示 AGY Offline。
5. HUD 附近 toast 提供安装 / 登录 / trust 提示。
6. 运行 build。

### 阶段 6：生命周期清理

1. HUD 退出时关闭 owned AGY。
2. 关闭 Enable Antigravity 时关闭 owned AGY。
3. 验证不杀用户自己的 `agy.exe`。
4. 运行 build。

---

## 17. 手动测试清单

请至少完成以下测试。

### 17.1 Codex 回归

- [ ] Antigravity 默认关闭。
- [ ] 启动 HUD 后不启动 `agy.exe`。
- [ ] Codex 额度仍能显示。
- [ ] Codex `7d` / `5h` reset time 仍正常。
- [ ] 折叠态吸附顶部仍正常。
- [ ] 悬停展开仍正常。
- [ ] 右键菜单仍正常。
- [ ] 托盘显示/隐藏仍正常。
- [ ] Refresh Now 仍正常。

### 17.2 设置

- [ ] Settings 窗口可以看到 Enable Antigravity。
- [ ] 默认关闭。
- [ ] 勾选后保存，重启仍保持。
- [ ] 取消勾选后保存，重启仍保持。
- [ ] 旧 settings.json 不含新字段时程序不崩溃。

### 17.3 AGY 未安装 / 未找到

- [ ] Enable Antigravity 后，如果找不到 `agy.exe`，Codex 仍正常。
- [ ] AGY row/card 显示 Offline 或 Failed。
- [ ] HUD 附近显示非模态提示。
- [ ] 提示不会每 30 秒重复刷屏。

### 17.4 AGY 首次未配置

- [ ] 如果 AGY 需要登录或 trust folder，隐藏启动失败时显示用户可读提示。
- [ ] 程序不崩溃。
- [ ] Codex 不受影响。

### 17.5 Managed AGY 成功

- [ ] Enable Antigravity 后，HUD 启动隐藏 `agy.exe`。
- [ ] 能发现 AGY 本地监听端口。
- [ ] 能调用 `RetrieveUserQuotaSummary`。
- [ ] 能解析 `gemini-weekly` 和 `gemini-5h`。
- [ ] 折叠态显示：`AGY [7d] [5h]`。
- [ ] 展开态显示 AGY card。
- [ ] AGY reset time 转换为本地时间。
- [ ] AGY Source 显示为 `Managed AGY`。

### 17.6 生命周期

- [ ] HUD 退出时关闭 HUD 自己启动的 AGY。
- [ ] 如果用户自己有一个 `agy.exe` 正在运行，HUD 退出时不能关闭它。
- [ ] 关闭 Enable Antigravity 后，HUD 自己启动的 AGY 被关闭。
- [ ] 不存在 `kill all agy.exe` 逻辑。

### 17.7 构建

- [ ] 每个阶段后运行：

```powershell
dotnet build .\CodexQuotaHud.csproj
```

- [ ] 最终 build 通过。

---

## 18. 推荐错误文案

请使用英文 UI 文案，以保持现有 Settings 窗口风格一致。

```text
AGY CLI not found. Please install Antigravity CLI and run agy once to finish setup.
```

```text
AGY started, but quota endpoint is not ready. Please run agy once manually to finish login/trust.
```

```text
Failed to read AGY quota. Please make sure Antigravity CLI is installed and signed in.
```

```text
AGY offline
```

```text
Source Managed AGY
```

---

## 19. 最终验收标准

本任务完成后，应满足：

1. 默认启动时行为与旧版基本一致，只是折叠态左侧多了 `Codex` 字样。
2. Antigravity 默认关闭，不会启动 `agy.exe`。
3. 用户在 Settings 中启用 Antigravity 后，HUD 会尝试隐藏启动并管理 `agy.exe`。
4. AGY 成功时显示 `AGY · Gemini` 模块，并使用 `7d` / `5h` 标签。
5. AGY 失败时不影响 Codex，且有 HUD 附近的非模态提示。
6. 折叠态支持 Codex + AGY 两行。
7. 展开态支持 Codex card + AGY card，两个模块边界清晰。
8. Codex 与 AGY 的数据读取逻辑已经模块化，未来新增 provider 不需要继续把协议逻辑塞进 `MainHudForm.cs`。
9. HUD 只关闭自己启动的 AGY，不关闭用户自己的 AGY。
10. `dotnet build .\CodexQuotaHud.csproj` 通过。

---

## 20. 最重要的约束总结

请始终遵守：

```text
保持吸附态 / 展开态设计不变。
Codex 和 AGY 都显示 7d / 5h。
AGY 使用 Managed AGY，由 HUD 启动并管理。
AGY 默认关闭。
AGY 隐藏启动，但首次配置失败时给用户提示。
AGY 不做 one-shot 冷启动查询。
只关闭 HUD 自己启动的 AGY。
不要 kill all agy.exe。
不要让 AGY 失败影响 Codex。
不要把 provider 协议细节塞进 MainHudForm。
每个阶段都运行 dotnet build。
```
