# Codex 任务：为 Managed AGY 增加进程诊断日志，定位 cmd/git.exe 弹窗来源

## 0. 任务背景

当前项目：`codex-quota-hub / codex-quota-hud`，Windows WinForms HUD。

用户测试发现：

1. 第一次打开软件时，偶尔会有一个 `cmd` 窗口一闪而过。
2. 退出后马上重新打开，通常没有这个闪窗。
3. 过几个小时后再打开，又可能出现一次 `cmd` 闪窗。
4. 运行过程中偶尔会出现 Windows 的 `git.exe` 报错弹窗。

当前判断：

- HUD 自己直接启动的外部程序主要有：
  - `codex app-server --listen stdio://`
  - `agy.exe`
  - `netstat -ano -p tcp`
- 这些直接启动点都已经设置 `UseShellExecute = false` / `CreateNoWindow = true` 或 hidden。
- 因此，闪窗和 `git.exe` 报错更可能来自 `agy.exe` 自己启动的子进程，例如自更新、环境检查、Git 检查、trust/login 初始化等。

本任务目标不是修复弹窗，而是**增加诊断日志，确认到底是谁启动了 `cmd.exe` / `git.exe` / `powershell.exe` 等进程**。

---

## 1. 本次修改目标

请只做“诊断日志增强”，不要改变主功能逻辑。

需要实现：

1. 在 Managed AGY 启动前后记录详细但安全的进程诊断日志。
2. 在 AGY 启动后的短时间内，持续监控并记录新出现的可疑进程。
3. 可疑进程包括：
   - `git.exe`
   - `cmd.exe`
   - `powershell.exe`
   - `pwsh.exe`
   - `conhost.exe`
   - `WindowsTerminal.exe`
   - `wt.exe`
   - `node.exe`
   - `agy.exe`
   - `codex.exe`
   - `netstat.exe`
4. 记录这些进程的：
   - 时间
   - PID
   - 进程名
   - Parent PID（如果能读取）
   - Parent Process Name（如果能读取）
   - 可执行文件路径（如果能读取）
   - 命令行（如果能读取，需截断）
   - StartTime（如果能读取）
5. 在 `AgyEndpointDiscovery` 调用 `netstat` 时记录 `netstat` 的启动/退出信息，避免把 `netstat` 误认为异常闪窗来源。
6. 不要记录敏感信息。

---

## 2. 建议新增文件

建议新增：

```text
src/Diagnostics/ProcessDiagnostics.cs
src/Diagnostics/DebugLogger.cs
```

如果你认为不需要拆成两个文件，也可以只新增一个 `ProcessDiagnostics.cs`，但必须保持代码清晰。

---

## 3. DebugLogger 要求

当前代码中多个类有自己的 `AppendLog` 方法，写入 `debug.log`。

本次建议新增一个统一的 `DebugLogger`：

```csharp
internal static class DebugLogger
{
    public static void Log(string message);
    public static void LogException(string prefix, Exception ex);
}
```

日志路径建议：

优先写入：

```text
%LOCALAPPDATA%\CodexQuotaHud\debug.log
```

如果失败，再 fallback 到当前目录：

```text
debug.log
```

日志格式：

```text
[2026-07-09 21:30:12.345] message
```

要求：

- 不要弹 MessageBox。
- 不要因为日志写入失败影响主程序。
- 任何日志异常都要吞掉。
- 单行日志尽量不要超过 1000 字符。

---

## 4. ProcessDiagnostics 要求

新增一个诊断工具类，例如：

```csharp
internal static class ProcessDiagnostics
{
    public static void LogInterestingProcesses(string reason);
    public static Task MonitorInterestingProcessesAsync(string reason, TimeSpan duration, TimeSpan interval, CancellationToken cancellationToken);
}
```

### 4.1 监控对象

只关注以下进程名，避免日志过大：

```text
git
cmd
powershell
pwsh
conhost
WindowsTerminal
wt
node
agy
codex
netstat
```

注意：`Process.ProcessName` 通常不带 `.exe`。

### 4.2 记录内容

每个进程记录一行，例如：

```text
[AGY-DIAG] new process reason=after-agy-start name=git pid=12345 ppid=6789 parent=agy path=C:\Program Files\Git\cmd\git.exe start=2026-07-09 21:30:12 cmd="git ..."
```

如果部分字段读取失败，用 `?`：

```text
ppid=? parent=? path=? cmd=?
```

### 4.3 Parent PID / CommandLine 获取方式

优先使用 WMI / CIM 查询 Windows 进程信息。

建议使用 `System.Management`：

- 如果项目当前没有引用，请在 `.csproj` 中添加：

```xml
<ItemGroup>
  <PackageReference Include="System.Management" Version="8.0.0" />
</ItemGroup>
```

然后通过 `Win32_Process` 查询：

```sql
SELECT ProcessId, ParentProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process
```

要求：

- WMI 查询失败时不要崩溃。
- 如果不想引入包，也可以只用 `System.Diagnostics.Process` 记录 PID / ProcessName / Path / StartTime，但 Parent PID 和 CommandLine 会不完整。优先实现 WMI，因为这次主要目标是确认 `git.exe` 的父进程。

### 4.4 命令行脱敏与截断

CommandLine 需要记录，但必须做基本保护：

- 截断到最多 500 字符。
- 移除换行。
- 不要记录环境变量。
- 不要记录 stdout/stderr 原文。
- 不要记录 quota JSON。
- 不要记录账号邮箱、token、csrf。

可以实现一个简单的 `SanitizeCommandLine`：

```text
replace \r/\n/tabs with spaces
collapse repeated spaces
truncate to 500 chars
```

如果命令行里出现 `csrf_token`、`token`、`authorization` 等关键词，尽量做简单打码。

---

## 5. ManagedAgyProcess.cs 需要增加的日志点

目标文件：

```text
src/Providers/Antigravity/ManagedAgyProcess.cs
```

### 5.1 EnsureRunningAsync 启动前

在 `process.Start()` 之前记录：

```text
[AGY-DIAG] ensure running
[AGY-DIAG] agy path=...
[AGY-DIAG] working dir=...
[AGY-DIAG] start hidden=True/False
[AGY-DIAG] CreateNoWindow=True/False WindowStyle=Hidden/Normal
```

然后调用：

```csharp
ProcessDiagnostics.LogInterestingProcesses("before-agy-start");
```

### 5.2 process.Start() 后立即记录

在 `process.Start()` 成功后，记录：

```text
[AGY-DIAG] agy managed process started pid=...
```

然后调用：

```csharp
ProcessDiagnostics.LogInterestingProcesses("immediately-after-agy-start");
```

### 5.3 AGY 启动后短时间监控

启动成功后，启动一个后台诊断任务，持续 30 秒，每 500ms 检查一次 interesting processes。

示例行为：

```csharp
_ = Task.Run(() => ProcessDiagnostics.MonitorInterestingProcessesAsync(
    reason: $"after-agy-start pid={process.Id}",
    duration: TimeSpan.FromSeconds(30),
    interval: TimeSpan.FromMilliseconds(500),
    cancellationToken: CancellationToken.None));
```

要求：

- 这个诊断任务不能阻塞 UI。
- 这个诊断任务不能影响 AGY 启动成功与否。
- 诊断任务内部必须吞掉异常。
- 日志里只记录新出现的 PID，避免每 500ms 重复刷同一个进程。

### 5.4 AGY 退出时

在 `ShutdownIfOwnedAsync()` 中，停止前后记录：

```text
[AGY-DIAG] stopping managed agy pid=...
[AGY-DIAG] stopped managed agy pid=...
[AGY-DIAG] stop timeout pid=...
```

当前代码可能已有类似日志；请统一到 `DebugLogger.Log`，并保留现有信息。

---

## 6. ManagedAgyQuotaProvider.cs 需要增加的日志点

目标文件：

```text
src/Providers/Antigravity/ManagedAgyQuotaProvider.cs
```

### 6.1 AGY endpoint discovery 前后

在开始 discovery 前记录：

```text
[AGY-DIAG] endpoint discovery start agyPid=...
```

找到端口时记录：

```text
[AGY-DIAG] endpoint ready agyPid=... port=...
```

失败时记录：

```text
[AGY-DIAG] endpoint discovery failed agyPid=... failureCount=...
```

如果 backoff 跳过 discovery，记录：

```text
[AGY-DIAG] endpoint discovery skipped by backoff next=...
```

### 6.2 HTTP 请求失败时

如果读取端口失败，目前可能只是 catch 后 `_port = null`。请增加简短日志：

```text
[AGY-DIAG] read from cached port failed port=... error=...
```

不要记录 response raw body。

---

## 7. AgyEndpointDiscovery.cs 需要增加的日志点

目标文件：

```text
src/Providers/Antigravity/AgyEndpointDiscovery.cs
```

在启动 `netstat` 前后记录：

```text
[AGY-DIAG] netstat start for pid=...
[AGY-DIAG] netstat started pid=...
[AGY-DIAG] netstat exit code=... ports=...
[AGY-DIAG] netstat timeout for pid=...
[AGY-DIAG] netstat failed error=...
```

注意：

- `netstat` 是 HUD 自己启动的诊断/端口发现进程。
- 它不应该弹窗，因为已有 `CreateNoWindow = true`。
- 记录这些日志有助于排除 `netstat` 被误认为 cmd 闪窗来源。

---

## 8. CodexQuotaReader.cs 可选日志增强

目标文件：

```text
src/Services/CodexQuotaReader.cs
```

Codex reader 已经是 one-shot hidden process。请保留原逻辑。

可选增强：

- 改用统一 `DebugLogger.Log`。
- 在启动 codex 前后记录：

```text
[CODEX-DIAG] starting codex app-server path=...
[CODEX-DIAG] codex app-server started pid=...
[CODEX-DIAG] codex app-server killed pid=...
```

不要记录 quota raw JSON。

---

## 9. 不要做的事情

本次任务不要做：

- 不要重构 UI。
- 不要改变 Codex quota 读取协议。
- 不要改变 AGY quota 解析逻辑。
- 不要改变刷新间隔。
- 不要默认打开 MessageBox。
- 不要自动上传日志。
- 不要记录完整 raw JSON。
- 不要记录账号邮箱、token、csrf。
- 不要 kill 用户手动启动的 `agy.exe`。

---

## 10. 测试步骤

请完成代码修改后运行：

```powershell
dotnet build .\CodexQuotaHud.csproj
```

然后手动测试：

### 10.1 基础启动测试

```powershell
dotnet run --project .\CodexQuotaHud.csproj
```

操作：

1. 打开 Settings。
2. 勾选 Enable Antigravity。
3. Save。
4. 等待 AGY card 正常显示或显示 Offline。
5. 右键 Exit。
6. 确认程序能正常退出。

### 10.2 日志检查

检查日志文件：

```text
%LOCALAPPDATA%\CodexQuotaHud\debug.log
```

如果没有，则检查当前目录：

```text
debug.log
```

应看到类似：

```text
[AGY-DIAG] ensure running
[AGY-DIAG] agy managed process started pid=...
[AGY-DIAG] process snapshot reason=before-agy-start ...
[AGY-DIAG] new process reason=after-agy-start name=...
[AGY-DIAG] netstat start for pid=...
[AGY-DIAG] endpoint ready agyPid=... port=...
```

### 10.3 观察 cmd/git 弹窗

如果再出现 `cmd` 闪窗或 `git.exe` 报错，请根据时间点查日志。

理想日志应能回答：

```text
git.exe 是不是在 AGY 启动后出现？
git.exe 的 parent PID 是不是 agy.exe？
cmd.exe 的 parent PID 是不是 agy.exe？
conhost.exe 是否伴随 cmd/git 出现？
```

---

## 11. 验收标准

完成后应满足：

1. `dotnet build .\CodexQuotaHud.csproj` 通过。
2. HUD 正常显示 Codex quota。
3. 启用 Antigravity 后，AGY quota 正常读取或显示合理 Offline。
4. 右键 Exit 正常退出，不残留 HUD 窗口。
5. 日志文件中能看到 AGY 启动前后的进程诊断信息。
6. 如果 `git.exe` / `cmd.exe` / `powershell.exe` 在 AGY 启动后出现，日志能记录其 PID、父进程、路径和命令行。
7. 日志写入失败不会影响主程序。

---

## 12. 修改完成后的回复格式

请在完成后回复：

```text
完成：已增加 AGY 进程诊断日志。

修改文件：
- ...

验证：
- dotnet build .\CodexQuotaHud.csproj 通过 / 未通过

日志位置：
- %LOCALAPPDATA%\CodexQuotaHud\debug.log

注意事项：
- ...
```
