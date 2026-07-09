# Codex Quota HUD：日志精简、移除 AGY 延迟启动、尝试禁用 Codex Plugins Git 同步

## 任务背景

当前项目已经实现：

- Codex quota 读取
- Managed AGY quota 读取
- session-based diagnostic logging
- Codex / AGY 进程诊断
- Codex Git 非交互环境变量
- AGY 生命周期清理

最近日志显示：

1. Codex app-server 每次启动时仍会触发：

```text
git ls-remote https://github.com/openai/plugins.git HEAD
```

该 Git 进程是 Codex app-server 的 descendant，不是 AGY 触发的。

2. OpenAI Codex 源码显示该 Git 调用来自 curated plugins startup sync。Codex 的 `plugins` 与 `remote_plugin` feature 默认启用。

3. 当前 AGY 启动前有一个 2000ms 的 cold-start-delay，用于避免 Codex 和 AGY 启动重叠，但这导致 HUD 启动整体变慢。现在需要移除该延迟。

4. 当前 Codex monitor 有一个日志设计问题：Codex app-server 已经被 kill 后，Codex monitor 仍继续运行一段时间，导致后续 AGY 进程被记录为：

```text
provider=codex phase=monitor event=new-process relation=unrelated name=agy
```

这会让日志混乱，需要修复。

5. 日志保留数量需要从当前值改为最近 15 个 session log。

本任务暂时不处理 `conhost.exe`。不要尝试通过复杂 Win32 API 或修改 Codex 源码来处理 `conhost.exe`。

---

## 总体目标

请完成以下修改：

1. session log 只保留最近 15 个 `debug-*.log`。
2. 单个 session log 仍保持最大 2 MB，超过后停止写入。
3. 修复 Codex monitor：Codex app-server 结束或被 kill 后，立即停止 Codex monitor，不要继续记录 AGY 相关进程。
4. 移除 AGY cold-start-delay，AGY 不再固定延迟 2000ms 启动。
5. Codex app-server 启动时优先尝试禁用 plugins：

```text
codex -c features.plugins=false -c features.remote_plugin=false app-server --listen stdio://
```

6. 如果 plugins-disabled 启动方式失败，自动回退到原始启动方式：

```text
codex app-server --listen stdio://
```

7. 保留已有 Git 非交互环境变量。
8. 日志明确记录 Codex 启动模式、启动参数、是否回退、是否仍发现 Git descendant。
9. 不修改用户全局 Codex 配置文件。
10. 不修改用户系统 PATH，只允许修改 HUD 启动的 Codex 子进程环境。

---

## 修改 1：日志保留数量改为 15 个

### 要求

日志目录继续使用：

```text
%LOCALAPPDATA%\CodexQuotaHud\logs\
```

每次启动仍创建独立 session log：

```text
debug-YYYY-MM-DD_HH-mm-ss.log
```

只保留最新 15 个 `debug-*.log`。

启动时或 logger 初始化时执行清理：

1. 枚举 `logs` 目录下的 `debug-*.log`。
2. 按文件名时间或 LastWriteTimeUtc 从新到旧排序。
3. 保留前 15 个。
4. 删除更旧的。
5. 删除失败不要让程序崩溃，只写一条简短 warning。

### 仍需保留

单个 session log 2 MB 限制继续保留：

```text
MaxSessionLogBytes = 2 * 1024 * 1024
```

超过后停止写入，并尽量写入一次：

```text
[LOG] Session log reached 2 MB limit. Further log entries are suppressed.
```

不要做 part2 / part3 分片。

---

## 修改 2：修复 Codex monitor 继续记录 AGY 的问题

### 当前问题

日志中出现过类似内容：

```text
provider=codex phase=monitor event=new-process relation=unrelated name=agy
```

原因是 Codex app-server 已经完成读取并被 kill，但 Codex monitor 仍然继续运行，后续 AGY 启动被 Codex monitor 记录到了。

### 新行为

Codex monitor 的生命周期必须绑定到本次 Codex app-server 进程。

当出现以下任一情况时，Codex monitor 应立即停止：

1. Codex app-server 已经退出。
2. Codex app-server 被 HUD kill。
3. Codex quota refresh 结束。
4. Codex refresh cancellation token 被取消。
5. HUD 正在退出。

### 实现建议

如果当前实现是 `Task.Run` 或类似后台 monitor，请引入 per-refresh `CancellationTokenSource`：

```csharp
using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
var monitorTask = ProcessDiagnostics.MonitorAsync(
    provider: "codex",
    targetPid: process.Id,
    duration: TimeSpan.FromSeconds(30),
    interval: TimeSpan.FromMilliseconds(500),
    cancellationToken: monitorCts.Token);
```

在 Codex app-server kill / dispose / refresh finished 后：

```csharp
monitorCts.Cancel();
await monitorTask.ConfigureAwait(false);
```

要求：

- monitor cancellation 不应抛出未处理异常。
- `OperationCanceledException` 应被视为正常结束。
- 日志可以写：

```text
[PROCESS-DIAG] provider=codex phase=monitor event=monitor-stop reason=codex-process-ended
```

或：

```text
[PROCESS-DIAG] provider=codex phase=monitor event=monitor-cancelled reason=codex-refresh-ended
```

### 验收标准

新版日志中不应再出现：

```text
provider=codex phase=monitor event=new-process relation=unrelated name=agy
```

---

## 修改 3：移除 AGY cold-start-delay

### 当前问题

之前为了避免 Codex / AGY 启动重叠，AGY 启动前加入了类似 2000ms delay。日志中会出现：

```text
provider=agy phase=cold-start-delay event=start delayMs=2000 reason=avoid-codex-agy-start-overlap
provider=agy phase=cold-start-delay event=stop delayMs=2000 reason=avoid-codex-agy-start-overlap
```

这会让 HUD 启动变慢。

### 新行为

删除该固定延迟。

Codex refresh 完成后，AGY refresh 应立即开始。

### 要求

1. 删除或禁用 cold-start-delay 逻辑。
2. 日志中不应再出现 `cold-start-delay`。
3. 不要为了替代它加入新的固定延迟。
4. 保留 AGY endpoint discovery backoff、not-ready threshold、managed process reuse 等已有逻辑。

---

## 修改 4：Codex app-server 优先以 plugins-disabled 模式启动

### 背景

当前 Git 调用来自 Codex 的 curated plugins startup sync。OpenAI Codex 源码中，`features.plugins` 与 `features.remote_plugin` 默认启用。HUD 只需要读取 `account/rateLimits/read`，不需要 Codex plugin 功能。

因此，HUD 启动 Codex app-server 时应优先尝试禁用 plugins。

### 新启动优先级

#### 优先启动方式

优先尝试：

```text
codex -c features.plugins=false -c features.remote_plugin=false app-server --listen stdio://
```

请注意参数顺序必须通过实际测试确认。若 Codex CLI 要求 `-c` 放在子命令之后，请按实际可用形式实现。目标是让本次 app-server 进程的有效配置中：

```text
features.plugins=false
features.remote_plugin=false
```

#### 回退启动方式

如果 plugins-disabled 启动方式失败，例如：

- Codex 立即退出
- initialize 超时
- app-server 无法响应
- stderr 表示参数不支持
- `account/rateLimits/read` 无法执行

则自动回退到原始启动方式：

```text
codex app-server --listen stdio://
```

### 日志要求

启动前写：

```text
[CODEX-DIAG] codex launch mode=plugins-disabled
[CODEX-DIAG] codex launch args=-c features.plugins=false -c features.remote_plugin=false app-server --listen stdio://
```

若成功：

```text
[CODEX-DIAG] codex launch mode=plugins-disabled succeeded
```

若失败并回退：

```text
[CODEX-DIAG] codex launch mode=plugins-disabled failed; falling back to legacy launch
[CODEX-DIAG] codex launch mode=legacy
[CODEX-DIAG] codex launch args=app-server --listen stdio://
```

若 legacy 成功：

```text
[CODEX-DIAG] codex launch mode=legacy succeeded
```

### 配置建议

在 `AppSettings` 中可以加入一个布尔项：

```text
DisableCodexPluginsForQuotaRead = true
```

默认值：

```text
true
```

当前不一定需要暴露到 Settings UI。可以先作为内部默认行为。

如果你决定暴露到 UI，文案可以是：

```text
Disable Codex plugins during quota read
```

但本任务不强制要求加 UI。

### 保留已有 Git 非交互变量

已有环境变量继续保留：

```text
GIT_TERMINAL_PROMPT=0
GCM_INTERACTIVE=false
GCM_GUI_PROMPT=false
GIT_ASKPASS=
SSH_ASKPASS=
```

这些变量不能阻止 Codex 调用 Git，但能减少 Git / GCM 弹窗概率。

### 不允许做的事情

不要修改用户全局 Codex 配置：

```text
%USERPROFILE%\.codex\config.toml
```

不要修改系统环境变量。

不要全局移除 Git PATH。

不要 kill 所有 `git.exe`。

不要引入 fake `git.exe` shim。

不要 patch 或替换用户的 Codex CLI。

---

## 修改 5：诊断日志增加 Git 检测总结

在每次 Codex refresh 结束时，建议输出一条总结日志：

```text
[CODEX-DIAG] codex child process summary gitDescendantSeen=false conhostDescendantSeen=true launchMode=plugins-disabled
```

字段建议：

```text
launchMode=plugins-disabled / legacy
usedFallback=true / false
gitDescendantSeen=true / false
conhostDescendantSeen=true / false
rateLimitReadSucceeded=true / false
```

本任务不要求处理 `conhost.exe`，但可以继续记录它。

---

## 测试步骤

请在完成修改后按以下步骤测试。

### Step 1：清理旧 HUD 进程

在 PowerShell 中执行：

```powershell
Get-Process CodexQuotaHud* -ErrorAction SilentlyContinue
```

如有旧进程，结束它们：

```powershell
Stop-Process -Name CodexQuotaHud -Force -ErrorAction SilentlyContinue
Stop-Process -Name CodexQuotaHud-win-x64-no-dotnet -Force -ErrorAction SilentlyContinue
```

如提示拒绝访问，请使用管理员 PowerShell。

### Step 2：构建

在项目根目录执行：

```powershell
dotnet build
```

要求：无编译错误。

### Step 3：运行新版程序

```powershell
dotnet run --project .\src\CodexQuotaHud.csproj
```

如果项目实际 csproj 路径不同，请按当前项目结构调整。

也可以直接运行 publish 产物或 release exe。

### Step 4：打开日志目录

```powershell
explorer "$env:LOCALAPPDATA\CodexQuotaHud\logs"
```

找到最新的：

```text
debug-YYYY-MM-DD_HH-mm-ss.log
```

### Step 5：检查 Codex 是否使用 plugins-disabled 模式

在最新日志中搜索：

```text
plugins-disabled
```

理想结果：

```text
[CODEX-DIAG] codex launch mode=plugins-disabled
[CODEX-DIAG] codex launch args=-c features.plugins=false -c features.remote_plugin=false app-server --listen stdio://
[CODEX-DIAG] codex launch mode=plugins-disabled succeeded
[CODEX-DIAG] codex rate limit response received
```

这说明新的启动方式可用，且没有破坏 quota 读取。

### Step 6：检查 Git 是否被阻止

在日志中搜索：

```text
openai/plugins.git
```

再搜索：

```text
name=git
```

再搜索：

```text
git ls-remote
```

理想结果：没有 Codex descendant Git，也没有：

```text
git ls-remote https://github.com/openai/plugins.git HEAD
```

如果仍出现 Git descendant，说明禁用 plugins 没有阻止 startup sync。请保留日志，下一步再考虑“只对 Codex 子进程移除 Git PATH”。本任务暂不做该方案。

### Step 7：检查 AGY 不再延迟启动

在日志中搜索：

```text
cold-start-delay
```

理想结果：没有任何结果。

检查 Codex 结束和 AGY 开始之间不应有固定 2000ms delay。

应类似：

```text
[CODEX-DIAG] codex app-server killed pid=xxxxx
[AGY-DIAG] ensure running
```

两者之间不应有 `delayMs=2000`。

### Step 8：检查 Codex monitor 不再混入 AGY

在日志中搜索：

```text
provider=codex phase=monitor
```

确认不再出现：

```text
provider=codex phase=monitor event=new-process relation=unrelated name=agy
```

如果仍出现，说明 Codex monitor 没有随 Codex refresh 结束正确取消。

### Step 9：检查 AGY 额度读取仍然正常

在日志中搜索：

```text
endpoint ready
```

理想结果：

```text
[AGY-DIAG] endpoint ready agyPid=xxxxx port=xxxx
```

HUD 上 AGY 的 7d / 5h 应正常显示。

### Step 10：检查退出清理

右键托盘图标，选择 Exit。

日志末尾应看到：

```text
[APP] exit requested
[APP] poller dispose started
[AGY-DIAG] stopping managed agy pid=xxxxx
[AGY-DIAG] stopped managed agy pid=xxxxx
[APP] poller dispose completed
[APP] session ended pid=xxxxx
```

### Step 11：检查日志数量限制

连续启动/退出 16 次，或复制旧日志模拟超过数量后，执行：

```powershell
Get-ChildItem "$env:LOCALAPPDATA\CodexQuotaHud\logs\debug-*.log" | Measure-Object
```

理想结果：

```text
Count <= 15
```

---

## 验收标准

必须满足：

1. `dotnet build` 成功。
2. HUD 能正常启动。
3. Codex quota 能正常显示。
4. AGY quota 能正常显示。
5. 日志目录最多保留最近 15 个 `debug-*.log`。
6. 单个 log 超过 2 MB 后停止写入。
7. 日志中不再出现 `cold-start-delay`。
8. 日志中不再出现 `provider=codex phase=monitor ... name=agy`。
9. Codex app-server 优先尝试 plugins-disabled launch。
10. plugins-disabled launch 失败时能自动回退 legacy launch。
11. 不修改用户全局 Codex 配置。
12. 不修改系统 PATH。
13. 不处理 `conhost.exe` 隐藏问题。

---

## 非目标

本任务不做：

- 不处理 `conhost.exe`。
- 不引入 Attach Only AGY 模式。
- 不引入 fake git shim。
- 不从 PATH 移除 Git。
- 不修改 OpenAI Codex CLI 源码。
- 不禁用 AGY。
- 不改 HUD UI 布局。
- 不改 Codex quota 协议。
- 不改 AGY quota 协议。
