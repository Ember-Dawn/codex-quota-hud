# Codex Quota HUD 修复任务：Exit 残影、AGY 生命周期与后台恢复

> 目标仓库：`E:\github\codex-quota-hud`  
> 执行对象：Codex 代码代理  
> 语言要求：请尽量使用中文注释/说明，但代码命名保持项目现有 C# 风格。  
> 重要：本任务是修复与稳定性优化，不要大规模重写 UI，不要改变当前“Codex + AGY · Gemini”卡片式视觉设计。

---

## 0. 背景与当前问题

当前项目已经完成以下能力：

- WinForms HUD；
- Codex provider；
- Managed AGY provider；
- AGY 通过隐藏启动的 `agy.exe` 读取本地 quota endpoint；
- 折叠态和展开态均支持 Codex / AGY 两个模块；
- 设置里支持 Enable Antigravity 和颜色配置；
- `7d` / `5h` 统一显示。

但测试发现一个严重退出 bug：

```text
使用 dotnet run 启动程序后，右键托盘菜单选择 Exit。
托盘图标消失，但 HUD 窗口仍残留在桌面上。
残留窗口无法移动、无法选中，托盘也没有对应图标。
```

这说明当前退出流程很可能卡在 UI 线程的释放逻辑中。尤其需要检查 `MainHudForm.Dispose()` 中是否同步等待异步释放，例如：

```csharp
_poller.DisposeAsync().AsTask().GetAwaiter().GetResult();
```

这种写法在 WinForms UI 线程中风险很高，容易造成退出时窗口残影、卡死或 AGY 进程清理不完整。

---

## 1. 总体修复目标

请完成以下修复：

1. 修复右键 `Exit` 后 HUD 残留、无法操作、托盘图标消失的问题。
2. 退出时可靠关闭 HUD 自己启动的 Managed AGY CLI。
3. 退出时不要误杀用户自己手动启动的 `agy.exe`。
4. 如果后台 Managed AGY CLI 意外退出，下一轮刷新应能自动重启并恢复读取。
5. 如果 AGY endpoint 长期不可用，应避免每次刷新都做昂贵 discovery。
6. 避免 UI 线程同步等待异步释放。
7. 减少长期运行时的资源 churn，例如反复创建 `HttpClient`。
8. 保持当前 UI 设计，不要做大规模视觉改版。

---

## 2. 必须优先修复：Exit 卡死 / 残影

### 2.1 不要在 `Dispose()` 中同步等待异步释放

如果当前代码里有类似：

```csharp
_poller.DisposeAsync().AsTask().GetAwaiter().GetResult();
```

请移除这种同步阻塞式释放。

`Dispose(bool disposing)` 里只做真正同步、快速、不会卡 UI 的释放，例如：

- 停止并 Dispose WinForms Timer；
- 隐藏并 Dispose NotifyIcon；
- Dispose 菜单；
- Dispose toast；
- Dispose CancellationTokenSource。

不要在 `Dispose()` 里等待 AGY 进程退出、HTTP 请求结束、端口 discovery 结束。

### 2.2 把 Exit 流程改成 async 安全退出

建议把当前 `ExitApplication()` 改造成 async 流程。示意逻辑：

```text
ExitApplicationAsync
  -> 如果已经在退出，直接 return
  -> 设置 _isExiting = true
  -> 停止 refresh timer
  -> 禁用菜单，避免重复点击
  -> 取消 _appCts
  -> 关闭 toast
  -> await _poller.DisposeAsync()
  -> 隐藏并释放 NotifyIcon
  -> BeginInvoke(Close) 或 Application.ExitThread()
```

注意：

- 不要先把托盘图标隐藏后再卡住。可以先禁用菜单，等清理完成后再隐藏托盘。
- 清理过程需要有超时保护，不能无限等待。
- 如果清理失败，也要继续关闭主窗口，不能留下不可操作窗口。

### 2.3 推荐实现方式

可以使用一个字段：

```csharp
private readonly CancellationTokenSource _appCts = new();
private bool _isExiting;
private bool _pollerDisposed;
```

右键菜单的 Exit 点击事件建议改成：

```csharp
_menu.Items.Add("Exit", null, async (_, _) => await ExitApplicationAsync());
```

`ExitApplicationAsync()` 里大致如下：

```csharp
private async Task ExitApplicationAsync()
{
    if (_isExiting)
    {
        return;
    }

    _isExiting = true;
    _refreshTimer.Stop();
    _appCts.Cancel();

    try
    {
        _menu.Enabled = false;
        _toast?.Close();
        _toast?.Dispose();
        _toast = null;

        await DisposePollerAsync();
    }
    catch
    {
        // 退出时不要因为清理失败而阻止关闭。
    }
    finally
    {
        _notifyIcon.Visible = false;
        BeginInvoke(new Action(Close));
    }
}
```

`DisposePollerAsync()` 应保证只执行一次：

```csharp
private async Task DisposePollerAsync()
{
    if (_pollerDisposed)
    {
        return;
    }

    _pollerDisposed = true;
    await _poller.DisposeAsync().ConfigureAwait(false);
}
```

如果你担心 `ConfigureAwait(false)` 后回到非 UI 线程，请只在非 UI 资源释放方法里使用；涉及 WinForms 控件的操作必须回到 UI 线程。

### 2.4 `FormClosing` 行为

保持这个语义：

- 用户点右上角关闭：隐藏到托盘，不退出；
- 用户右键托盘 Exit：真正退出。

示意：

```csharp
private void MainHudForm_FormClosing(object? sender, FormClosingEventArgs e)
{
    if (!_isExiting && e.CloseReason == CloseReason.UserClosing)
    {
        e.Cancel = true;
        Hide();
    }
}
```

如果 `_isExiting == true`，不要取消关闭。

---

## 3. 刷新流程需要支持取消

### 3.1 MainHudForm 增加应用级 CancellationToken

`RefreshQuotaAsync()` 调用 `_poller.RefreshAsync()` 时，应传入 `_appCts.Token`。

示意：

```csharp
var snapshots = await _poller.RefreshAsync(_appCts.Token);
```

在退出时：

```csharp
_appCts.Cancel();
```

这样退出时如果正在进行 AGY discovery、HTTP 请求或 Codex 读取，可以尽快取消，而不是继续卡住。

### 3.2 刷新中遇到取消不要弹错误 toast

如果是退出导致的 `OperationCanceledException`，不要显示 `Failed` / toast。

建议：

```csharp
catch (OperationCanceledException) when (_isExiting || _appCts.IsCancellationRequested)
{
    return;
}
```

---

## 4. QuotaPoller 的释放逻辑

### 4.1 避免 Dispose 与 Refresh 并发竞态

`QuotaPoller` 当前有 `_refreshGate`。请确保 Dispose 时不会和 Refresh 同时操作 provider。

推荐做法：

```text
DisposeAsync
  -> 等待 _refreshGate
  -> dispose codex provider
  -> dispose agy provider
  -> release gate
  -> dispose gate
```

注意避免以下问题：

- Refresh 正在执行时 Dispose 同时释放 provider；
- Dispose 过程中 Refresh 又进入；
- `_refreshGate.Dispose()` 时仍有等待者。

可以加字段：

```csharp
private bool _disposed;
```

`RefreshAsync` 开头如果 disposed，返回空数组或抛 `ObjectDisposedException`，根据项目风格选择。推荐返回空数组，避免退出时报错。

---

## 5. Managed AGY 退出清理

### 5.1 只关闭 HUD 自己启动的 AGY

继续保持原则：不要 `kill all agy.exe`。

关闭 AGY 时必须确认：

- 有 `_process` 对象；
- 进程未退出；
- 进程名仍然是 `agy`；
- 启动时间匹配；
- 可执行路径匹配；
- 该进程确实是 HUD 启动的。

如果当前 `WasStartedByHud` 是：

```csharp
public bool WasStartedByHud => _process is not null;
```

建议改为显式字段：

```csharp
private bool _wasStartedByHud;
public bool WasStartedByHud => _wasStartedByHud;
```

当 `process.Start()` 成功后设置：

```csharp
_wasStartedByHud = true;
```

当 Dispose 清理完对象后重置：

```csharp
_wasStartedByHud = false;
```

这样未来如果增加 attach 用户已有 AGY 的功能，不会误判。

### 5.2 尊重 `CloseManagedAgyOnExit`

`AppSettings` 中已有：

```csharp
public bool CloseManagedAgyOnExit { get; set; } = true;
```

如果保留这个设置，请在关闭 AGY 时实际使用它。

推荐逻辑：

```csharp
public async ValueTask DisposeAsync()
{
    if (_settings.CloseManagedAgyOnExit)
    {
        await ShutdownIfOwnedAsync().ConfigureAwait(false);
    }

    await DisposeExistingProcessObjectAsync().ConfigureAwait(false);
}
```

如果你认为目前不需要这个设置，也可以删除该设置字段和相关保存逻辑。但更推荐保留并实现，因为这对调试有用。

### 5.3 关闭 AGY 必须有超时，不得阻塞 UI

关闭 AGY 时可以继续使用：

```csharp
process.Kill(entireProcessTree: true);
await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
```

但这段不能在 UI 线程同步等待。请确保调用链是 async await，不要 `.Result` / `.Wait()` / `.GetAwaiter().GetResult()`。

---

## 6. 后台 AGY 意外退出后的补救

当前方向是对的：`EnsureRunningAsync()` 如果发现 `_process.HasExited`，应释放旧对象并重新启动 `agy.exe`。

请确认并完善以下行为：

```text
Refresh AGY
  -> EnsureRunningAsync
     -> 如果托管 AGY 仍在运行：复用
     -> 如果托管 AGY 已退出：清理旧 Process 对象，重新启动
  -> 如果缓存端口失效：清空端口并重新 discovery
  -> 如果 endpoint ready：恢复 Ok
  -> 如果 endpoint 不 ready：返回 Offline，并显示错误提示
```

如果 AGY 意外退出，下一轮 refresh 应该能自动启动一个新的 Managed AGY。

---

## 7. AGY endpoint 长期不可用时加 backoff

当前如果 endpoint 长期不可用，每次刷新都可能进行最多 15 秒的端口 discovery。这会造成：

- 刷新卡顿；
- 退出时更容易卡住；
- 资源浪费；
- 隐藏 agy 卡在 login/trust 时反复尝试。

请增加 backoff。

建议字段：

```csharp
private DateTime _nextEndpointDiscoveryAt = DateTime.MinValue;
private int _endpointFailureCount;
```

建议逻辑：

```text
如果 _port 有值：先快速尝试读该端口。
如果失败：清空 _port。

如果现在时间还没到 _nextEndpointDiscoveryAt：
  直接返回 Offline / endpoint not ready，不做完整 discovery。

如果允许 discovery：
  执行 discovery。
  成功：重置 failure count，记录 port。
  失败：failure count++，设置下一次 discovery 时间。
```

backoff 间隔建议：

```text
第 1 次失败：15 秒
第 2 次失败：30 秒
第 3 次及以后：60 秒
```

注意：

- 用户手动 Refresh Now 可以绕过 backoff，或者至少可以缩短等待。若实现复杂，可以暂时不区分手动/自动刷新。
- 设置中关闭/重新开启 Antigravity 时，应重置 backoff。

---

## 8. 连续 endpoint not ready 后处理隐藏 AGY

如果隐藏启动 AGY 后连续多次 endpoint not ready，说明 AGY 可能卡在首次登录、trust folder 或 TUI 初始化。

建议增加计数：

```csharp
private int _endpointNotReadyCount;
```

如果连续 3 次完整 discovery 都失败：

```text
关闭 HUD 自己启动的 AGY 进程；
清空端口；
返回 Offline；
错误提示用户：Please run agy once manually to finish login/trust.
```

这样可以避免隐藏的 `agy.exe` 长期挂着但不可用。

---

## 9. 复用 AGY HttpClient

当前如果 `ReadFromPortAsync` 每次都创建：

```csharp
using var handler = new HttpClientHandler { ... };
using var client = new HttpClient(handler) { Timeout = timeout };
```

建议改为 provider 级别复用。

推荐：

```csharp
private readonly HttpClient _httpClient;
```

构造函数里创建：

```csharp
var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
        IsLocalAgyEndpoint(request?.RequestUri, certificate)
};
_httpClient = new HttpClient(handler);
```

每次请求时不要改全局 `Timeout`，而是使用 `CancellationTokenSource` 做单次超时：

```csharp
using var timeoutCts = new CancellationTokenSource(timeout);
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
using var response = await _httpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
```

在 `DisposeAsync` 或同步 Dispose 中释放 `_httpClient`。

证书绕过仍然必须限制在：

```text
https://127.0.0.1
```

不要全局关闭 TLS 校验。

---

## 10. netstat discovery 优化

`AgyEndpointDiscovery` 当前通过 `netstat -ano -p tcp` 找监听端口。可以保留这个方案。

但请确保：

- `netstat` 子进程始终隐藏；
- 读取 stdout/stderr 不会死锁；
- 有 3 秒左右超时；
- 取消时能杀掉 netstat；
- 解析只接受目标 PID 的 `LISTENING` / `LISTEN` TCP 端口。

Windows netstat 英文通常显示 `LISTENING`，当前如果只判断 `LISTEN` 也可以匹配 `LISTENING`，不要改坏。

---

## 11. UI 刷新与退出交互

### 11.1 退出时不要继续更新 UI

在 `RefreshQuotaAsync` 结束后更新 UI 前，检查：

```csharp
if (_isExiting || IsDisposed || Disposing)
{
    return;
}
```

避免退出中刷新回调又操作控件。

### 11.2 Timer Tick 退出期间直接返回

```csharp
_refreshTimer.Tick += async (_, _) =>
{
    if (_isExiting) return;
    await RefreshQuotaAsync();
};
```

### 11.3 Toast 退出时关闭并置空

退出时：

```csharp
_toast?.Close();
_toast?.Dispose();
_toast = null;
```

`Dispose()` 中也可以防御性释放，但不要重复导致异常。

---

## 12. 日志建议

继续写 `debug.log` 可以，但不要打印敏感信息。

建议增加以下日志，方便验证：

```text
exit requested
refresh cancelled by exit
poller dispose started
poller dispose completed
agy managed process stopping pid=...
agy managed process stopped pid=...
agy managed process stop timeout pid=...
agy endpoint discovery skipped by backoff
agy endpoint discovery failed count=...
agy endpoint rediscovered port=...
```

不要打印：

- 完整 command line；
- 完整 raw JSON；
- token；
- 用户账号信息。

---

## 13. 测试清单

请完成修改后运行：

```powershell
dotnet build .\CodexQuotaHud.csproj
```

然后手动测试：

### 13.1 基础启动

```powershell
dotnet run
```

确认：

- HUD 正常显示；
- Codex card 正常刷新；
- 如果 Enable Antigravity 打开，AGY card 正常显示或显示合理错误；
- 托盘图标存在。

### 13.2 Exit 测试，必须通过

右键托盘图标 → `Exit`。

必须满足：

- HUD 窗口完全消失；
- 托盘图标完全消失；
- 不留下无法选中/无法移动的残影窗口；
- `CodexQuotaHud.exe` 进程退出；
- 如果 AGY 是 HUD 托管启动的，`agy.exe` 也应退出。

PowerShell 检查：

```powershell
Get-Process CodexQuotaHud -ErrorAction SilentlyContinue
Get-Process agy -ErrorAction SilentlyContinue
```

注意：如果用户本来就手动开了其他 `agy.exe`，不要把用户自己的 AGY 误杀。

### 13.3 右上角关闭按钮行为

点击 HUD 右上角关闭，或系统 close：

- 应隐藏到托盘；
- 不应退出进程；
- 托盘点击可恢复显示。

### 13.4 AGY 意外退出恢复

启用 Antigravity 后运行 HUD。

找到 HUD 启动的 AGY 进程后手动结束：

```powershell
Get-Process agy
Stop-Process -Id <pid>
```

等待下一轮刷新。

预期：

- HUD 不崩溃；
- 下一轮或稍后自动重新启动 Managed AGY；
- endpoint ready 后 AGY quota 恢复；
- 如果 endpoint 不 ready，显示合理 Offline/Failed 提示。

### 13.5 AGY endpoint 不可用

模拟未登录或 trust 未完成时：

- HUD 不应每次刷新都卡很久；
- 应有 backoff；
- 不应不断启动多个 `agy.exe`；
- 连续失败后应关闭自己启动的不可用 AGY，提示用户手动运行 agy 完成 setup。

### 13.6 重复 Exit

快速多次点击 Exit，或者 Exit 时刷新正在进行。

预期：

- 不抛异常；
- 不残留窗口；
- 不残留托盘图标；
- 不残留 HUD 托管 AGY。

---

## 14. 不要做的事情

请不要：

- 不要把 Managed AGY 改成每次查询启动/销毁的 one-shot 模式；
- 不要 `kill all agy.exe`；
- 不要全局关闭 TLS 校验；
- 不要在 UI 线程 `.Wait()` / `.Result` / `.GetAwaiter().GetResult()` 等待异步任务；
- 不要大规模重写 UI；
- 不要引入 Electron/Tauri/WebView；
- 不要添加复杂历史图表或额度预测功能；
- 不要打印完整 raw JSON 或敏感信息到日志。

---

## 15. 推荐修改文件

优先检查和修改：

```text
src/UI/MainHudForm.cs
src/Services/QuotaPoller.cs
src/Providers/Antigravity/ManagedAgyQuotaProvider.cs
src/Providers/Antigravity/ManagedAgyProcess.cs
src/Providers/Antigravity/AgyEndpointDiscovery.cs
src/Services/AppSettings.cs
src/Services/SettingsStore.cs
```

通常不需要改：

```text
src/UI/ProviderCardControl.cs
src/UI/CollapsedProviderRowControl.cs
src/UI/QuotaBarControl.cs
src/Providers/Codex/CodexQuotaProvider.cs
src/Services/CodexQuotaReader.cs
```

除非测试发现 UI 退出或刷新仍有问题。

---

## 16. 交付要求

完成后请给出：

1. 修改摘要；
2. 重点说明 Exit 残影 bug 如何修复；
3. 说明 AGY 退出清理逻辑；
4. 说明 AGY 意外退出后的恢复逻辑；
5. 说明是否运行了 `dotnet build .\CodexQuotaHud.csproj`；
6. 如果有未能验证的地方，请明确列出。

