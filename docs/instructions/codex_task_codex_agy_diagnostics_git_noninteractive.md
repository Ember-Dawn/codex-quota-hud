# Codex Quota HUD 修复任务：统一进程诊断、Codex Git 非交互、AGY 闪窗抑制

> 目标项目：`codex-quota-hud` / `codex-quota-hub`
>
> 目标平台：Windows / .NET 8 / WinForms
>
> 本文档用于直接交给 Codex 执行。

---

## 0. 背景与已观察到的问题

当前软件在运行时存在两个相关体验问题：

1. 第一次打开 HUD，或者退出后间隔几个小时再打开时，可能出现一个 `cmd` / console 窗口一闪而过。
2. 运行过程中偶尔出现 Windows 的 `git.exe` 报错弹窗。

最近加入的诊断日志已经确认：

- `git.exe` 报错大概率不是 AGY 触发的，而是 Codex app-server 内部触发的。
- 日志中出现过：

```text
parent=codex
cmd="git ls-remote https://github.com/openai/plugins.git HEAD"
```

- AGY 启动后也会生成若干子进程，例如：

```text
agy.exe --bg-updater
agy.exe --version
node.exe ... playwright ... --version
conhost.exe parent=agy
```

因此，下一步需要做三类修复：

1. 把进程诊断日志抽象成通用模块，同时监控 Codex 与 AGY。
2. 给 Codex app-server 的启动环境加入 Git / GCM 非交互变量，尽量避免 `git.exe` GUI 弹窗。
3. 在当前 Managed AGY 方案上尽量减少启动期 cmd / conhost 闪窗，不新增 Attach Only AGY 模式。

---

## 1. 本次任务范围

请完成以下内容：

```text
1. 新增或重构通用 ProcessDiagnostics 模块。
2. Codex refresh 期间启用进程诊断。
3. AGY refresh / startup / shutdown 期间继续启用进程诊断，但命名改为通用格式。
4. Codex app-server 启动时设置 Git / GCM 非交互环境变量。
5. AGY 启动增加轻量 startup delay，避免与 Codex 同时启动。
6. AGY ready 后尽量复用同一进程，不因短暂 endpoint 失败频繁重启。
7. 保留当前 AGY hidden 启动设置：UseShellExecute=false, Redirect stdout/stderr, CreateNoWindow=true, WindowStyle=Hidden。
8. 日志必须明确标记 git/cmd/conhost/node/pwsh 等进程是 Codex descendant 还是 AGY descendant。
```

本次不要做：

```text
不要新增 Attach Only AGY 模式。
不要新增 App Local Provider。
不要大改 UI 外观。
不要改变 Codex quota 协议。
不要改变 AGY quota endpoint 协议。
不要 kill all agy.exe。
不要把 token、credential、完整敏感环境变量写入日志。
```

---

## 2. 通用 ProcessDiagnostics 模块

### 2.1 新增建议文件

建议新增：

```text
src/Diagnostics/ProcessDiagnostics.cs
src/Diagnostics/ProcessInfoSnapshot.cs
src/Diagnostics/DiagnosticsLogger.cs
```

如果项目当前没有 `Diagnostics` 文件夹，请创建。

也可以根据现有项目风格放到 `src/Services`，但建议独立出来。

### 2.2 日志位置

诊断日志统一写到：

```text
%LOCALAPPDATA%\CodexQuotaHud\debug.log
```

即：

```csharp
Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "CodexQuotaHud",
    "debug.log")
```

如果该路径创建或写入失败，再 fallback 到：

```text
Environment.CurrentDirectory\debug.log
```

请替换当前散落在不同类里的 `AppendLog`，避免有的日志写到项目目录，有的写到 LocalAppData。

### 2.3 日志格式

统一使用如下格式：

```text
[yyyy-MM-dd HH:mm:ss.fff] [PROCESS-DIAG] provider=codex phase=before-start event=snapshot count=...
[yyyy-MM-dd HH:mm:ss.fff] [PROCESS-DIAG] provider=codex phase=after-start event=process relation=descendant name=git pid=... ppid=... parent=codex path=... start=... cmd=...
[yyyy-MM-dd HH:mm:ss.fff] [PROCESS-DIAG] provider=agy phase=after-start event=new-process relation=descendant name=conhost pid=... ppid=... parent=agy path=... start=... cmd=...
```

字段建议：

```text
provider      codex / agy / app
phase         before-start / after-start / monitor / before-kill / after-kill / before-shutdown / after-shutdown / endpoint-discovery
reason        可选，补充说明
relation      target / descendant / related / unrelated
name          进程名
pid           进程 PID
ppid          父进程 PID
parent        父进程名
path          可读取时写进程路径，不可读取时写 ?
start         可读取时写启动时间，不可读取时写 ?
cmd           可读取时写命令行，不可读取时写 ?
```

### 2.4 重点监控进程名

至少监控这些进程：

```text
codex
CodexQuotaHud
CodexQuotaHud-win-x64-no-dotnet
CodexQuotaHud-win-x64-with-dotnet
agy
conhost
cmd
powershell
pwsh
git
git-remote-https
node
python
netstat
```

注意：Windows 上 `git remote-https` 可能表现为：

```text
git.exe remote-https ...
git-remote-https.exe
```

解析时不要只匹配一个名字。

### 2.5 进程树关系判断

请实现一个尽量可靠的 descendant 判断：

```text
targetPid = Codex app-server PID 或 AGY 主进程 PID

对于每个进程：
  读取 pid / ppid
  递归查 parent chain
  如果 parent chain 中出现 targetPid，则 relation=descendant
  如果 pid == targetPid，则 relation=target
  如果只是名字相关但不是后代，则 relation=related
  否则 relation=unrelated
```

日志中不要只靠进程名判断来源。必须尽量使用 PID / PPID 链判断。

### 2.6 监控窗口

为 Codex 和 AGY 都提供短时间 monitor：

```text
durationMs = 30000
intervalMs = 500
```

monitor 只记录“新增进程”，避免每 500 ms 重复刷同一批进程。

如果实现成本较高，可以先记录：

```text
before-start snapshot
after-start snapshot
monitor new-process only
before-kill / after-kill snapshot
```

---

## 3. Codex app-server 诊断与 Git 非交互变量

### 3.1 Codex refresh 期间启用诊断

在 `CodexQuotaReader` 或当前负责启动 Codex app-server 的类里加入：

```text
before-codex-start snapshot
codex started pid=...
immediately-after-codex-start snapshot
codex monitor 30s
before-codex-kill snapshot
after-codex-kill snapshot
```

日志 tag 使用：

```text
[CODEX-DIAG]
[PROCESS-DIAG] provider=codex ...
```

建议保留已有 `[CODEX-DIAG]` 简要日志，同时用 `[PROCESS-DIAG]` 记录进程树。

### 3.2 给 Codex app-server 设置非交互环境变量

在 Codex app-server 的 `ProcessStartInfo` 中设置以下环境变量：

```text
GIT_TERMINAL_PROMPT=0
GCM_INTERACTIVE=false
GCM_GUI_PROMPT=false
GIT_ASKPASS=
SSH_ASKPASS=
```

如当前使用：

```csharp
var startInfo = new ProcessStartInfo
{
    FileName = codexPath,
    UseShellExecute = false,
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = true,
    StandardOutputEncoding = Encoding.UTF8,
    StandardErrorEncoding = Encoding.UTF8
};
```

请在 `StartInfo.Environment` 中加入上述变量。

注意：

```text
1. 不要清空整个 Environment。
2. 只追加 / 覆盖这几个 Git 相关变量。
3. 不要把完整 Environment 写入日志。
4. 可以在日志中写一行：codex git non-interactive env applied。
```

### 3.3 预期效果

这不会阻止 Codex 内部调用 Git，但应尽量避免 Git Credential Manager 或 Git GUI prompt 弹出窗口。

如果 Git 访问失败，应尽量表现为：

```text
Codex stderr / debug.log 中记录错误
HUD 不弹 Windows GUI 报错框
```

---

## 4. AGY 闪窗抑制：保留 Managed AGY，但降低启动干扰

### 4.1 保留当前隐藏启动设置

AGY 主进程启动仍使用：

```text
UseShellExecute=false
RedirectStandardOutput=true
RedirectStandardError=true
CreateNoWindow=true
WindowStyle=Hidden
```

不要改成可见窗口。

### 4.2 AGY 启动延迟

当前 Codex refresh 和 AGY refresh 可能同时发生。请调整逻辑，使 AGY 启动尽量不要和 Codex app-server 启动重叠。

建议：

```text
如果 AGY 当前未运行，且本轮 refresh 同时需要启动 Codex 与 AGY：
  先启动并完成 Codex quota refresh
  再延迟 2-5 秒启动 AGY
```

可实现为：

```text
QuotaPoller.RefreshAsync 中不要 Task.WhenAll 同时启动 Codex 与 AGY。
改为：
  1. await Codex refresh
  2. 如果 EnableAntigravity:
       如果 AGY provider 尚未运行，await Task.Delay(2000, cancellationToken)
       await AGY refresh
```

如果担心刷新时间变长，可以只在 AGY 需要 cold start 时延迟；如果 AGY 已经运行且有 cached port，则不延迟。

请为 `ManagedAgyQuotaProvider` 暴露只读状态，例如：

```csharp
public bool IsProcessRunning => _process.IsRunning;
public bool HasCachedEndpoint => _port.HasValue;
public bool NeedsColdStart => !_process.IsRunning && !_port.HasValue;
```

然后在 poller 中判断。

### 4.3 AGY ready 后尽量复用

AGY endpoint ready 后，应尽量复用同一个 AGY 进程和 cached port。

不要因为以下情况立刻 kill / restart AGY：

```text
单次 netstat 为空
单次 HTTP timeout
单次 endpoint request 失败
```

建议：

```text
1. cached port 失败一次：清空 port，但不 kill AGY。
2. endpoint discovery 连续失败：进入 backoff。
3. 连续多次 not ready 才考虑 shutdown 自己启动的 AGY。
4. shutdown 后不要立即重启，必须遵守 backoff。
```

如果当前已有类似逻辑，请检查是否仍会频繁 restart AGY。

### 4.4 AGY 子进程诊断

AGY 启动后继续记录：

```text
agy main process pid
agy conhost descendant
agy --bg-updater descendant
agy --version descendant
node / playwright descendant
cmd / powershell / pwsh descendant
```

并在日志中明确写：

```text
relation=descendant provider=agy
```

这样下次可以判断 cmd 闪窗到底来自：

```text
Codex app-server
AGY main process
AGY bg-updater
AGY version check
Playwright node check
其他程序
```

### 4.5 AGY stdout/stderr 日志

当前 stdout/stderr drain 只是读取后丢弃。请修改为可选地写入 debug log，但要注意：

```text
1. 默认只写前 180-300 个字符。
2. 不写 token / credential / auth URL / secret。
3. 如果疑似敏感内容，用 [redacted]。
4. 日志前缀为 [AGY-STDOUT] / [AGY-STDERR]。
```

如果不确定是否会泄露敏感信息，可以只在诊断开关开启时记录。

---

## 5. QuotaPoller 并发策略调整

当前如果使用 `Task.WhenAll` 同时刷新 Codex 和 AGY，容易导致：

```text
codex -> git / conhost
agy   -> conhost / bg-updater / node
```

在启动阶段重叠，从而让用户看到更多闪窗，也让日志难以判断。

请改为更稳定的顺序：

```text
RefreshAsync:
  snapshots = []
  codexSnapshot = await codexProvider.RefreshAsync(ct)
  snapshots.Add(codexSnapshot)

  if EnableAntigravity:
      ensure agyProvider
      if agyProvider.NeedsColdStart:
          log agy cold start delay
          await Task.Delay(2000 or 3000, ct)
      agySnapshot = await agyProvider.RefreshAsync(ct)
      snapshots.Add(agySnapshot)

  return snapshots
```

要求：

```text
1. 保留 SemaphoreSlim 防重入。
2. 支持 cancellation token。
3. 退出时不能因为 delay 卡住。
4. 如果 Codex refresh 失败，仍然可以继续尝试 AGY refresh，除非 cancellation requested。
5. 如果 AGY refresh 失败，不影响 Codex 显示。
```

---

## 6. 设置项与诊断开关

如果当前没有诊断开关，可以先默认启用 process diagnostics，但建议避免日志无限膨胀。

最低要求：

```text
1. debug.log 单文件超过 5 MB 时轮转为 debug.old.log。
2. 每次写日志时检查大小，或启动时检查大小。
3. 日志写入失败不得影响 HUD 正常运行。
```

可选但推荐：

```text
新增 AppSettings.EnableProcessDiagnostics = true
```

但本次不要求 UI 加开关。可以只作为配置字段，默认 true。

---

## 7. 验收标准

### 7.1 Build

必须通过：

```powershell
dotnet build
```

以及：

```powershell
dotnet run
```

### 7.2 Codex Git 非交互验证

启动 HUD 后，观察：

```text
%LOCALAPPDATA%\CodexQuotaHud\debug.log
```

应出现：

```text
[CODEX-DIAG] codex git non-interactive env applied
[PROCESS-DIAG] provider=codex ...
```

如果 Codex 仍然启动 `git ls-remote https://github.com/openai/plugins.git HEAD`，日志应能记录：

```text
relation=descendant provider=codex name=git ...
```

并且最好不要再出现 Windows GUI `git.exe` 报错框。

### 7.3 AGY 闪窗诊断验证

启用 Antigravity 后，日志应能记录：

```text
[PROCESS-DIAG] provider=agy phase=after-start event=new-process relation=descendant name=conhost ...
[PROCESS-DIAG] provider=agy ... name=agy cmd="... --bg-updater ..."
[PROCESS-DIAG] provider=agy ... name=node cmd="... playwright ... --version"
```

如果用户看到 cmd 一闪而过，应能通过日志判断该窗口来自 Codex 还是 AGY。

### 7.4 AGY 启动延迟验证

当 Codex 与 AGY 都需要 cold start 时，日志顺序应类似：

```text
provider=codex phase=before-start
provider=codex phase=after-kill
provider=agy phase=cold-start-delay
provider=agy phase=before-start
```

不要让 Codex 和 AGY 第一次启动完全并发。

### 7.5 AGY 生命周期验证

以下行为必须保持：

```text
1. AGY endpoint ready 后继续复用同一 agy.exe。
2. cached port 单次失败不立刻 kill AGY。
3. endpoint discovery 失败进入 backoff。
4. HUD 正常 Exit 时，只关闭 HUD 自己启动的 AGY。
5. 不 kill 用户手动启动的其他 agy.exe。
```

### 7.6 UI 验证

不要破坏现有 UI：

```text
1. 折叠态仍显示 Codex / AGY。
2. 展开态仍显示 Codex card 和 AGY card。
3. AGY 正常读取时不显示 Source footer。
4. Settings 中 Enable Antigravity 仍可用。
5. Refresh Now 不导致窗口假死。
6. Exit 后无残影，进程正常退出。
```

---

## 8. 额外注意事项

### 8.1 不要写敏感信息

日志中不要写：

```text
auth token
cookie
credential
full environment variables
OAuth URL 中的 token
完整 secret 参数
```

命令行中如果包含可疑敏感字段，请打码：

```text
--token=xxx        -> --token=[redacted]
access_token=xxx  -> access_token=[redacted]
Authorization:    -> Authorization: [redacted]
```

### 8.2 不要误杀外部进程

AGY shutdown 只能针对 HUD 自己启动且身份匹配的进程：

```text
pid matches
process name is agy
start time matches
executable path matches
wasStartedByHud == true
```

不要使用：

```text
taskkill /IM agy.exe /F
kill all agy.exe
```

### 8.3 不要让诊断影响正常功能

进程查询、WMI、命令行读取失败时，应：

```text
写 ? 或跳过该字段
不要抛异常到 UI
不要影响 quota refresh
```

---

## 9. 完成后请输出

请完成代码修改后，在回复中说明：

```text
1. 修改了哪些文件。
2. Codex Git 非交互变量具体在哪里设置。
3. ProcessDiagnostics 如何判断 descendant。
4. AGY 启动延迟和复用逻辑如何实现。
5. debug.log 保存路径。
6. dotnet build 是否通过。
```

