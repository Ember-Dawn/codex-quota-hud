# Codex Quota HUD

Codex Quota HUD 是一个轻量级 Windows 桌面 HUD，用于在本机查看 **Codex** 与可选的 **Antigravity / AGY** 额度剩余情况。

项目使用 **C# + WinForms** 构建，目标是保持小巧、原生、可维护：在桌面上显示紧凑的额度条，支持顶部吸附、悬停展开、托盘菜单、手动刷新、自动刷新、颜色配置，以及可选的 Managed AGY 后台额度读取。

> 说明：Antigravity / AGY 额度读取依赖本地 CLI 行为与非官方本地接口，可能随 Antigravity 更新而变化。Codex 额度读取依赖本地 Codex CLI app-server 接口。

---

## 项目目标

本项目的核心目标是：

1. 从本机命令行工具读取额度信息。
2. 将不同来源的额度统一成内部模型。
3. 用轻量、原生、紧凑的 Windows HUD 显示额度。

当前支持：

- **Codex**：默认启用，通过本机 Codex CLI 读取额度。
- **AGY / Antigravity Gemini**：可选启用，由 HUD 托管启动隐藏的 `agy.exe` 并读取本地 quota endpoint。

本项目不追求复杂化，不默认实现：

- 额度节奏建议。
- 历史预测。
- 长期统计图表。
- 账号管理面板。
- 云同步。
- Electron / Tauri / WebView 重写。

---

## 主要功能

- 原生 Windows HUD，基于 WinForms。
- 支持 **展开态** 与 **吸附折叠态**。
- 支持拖动到屏幕顶部自动吸附。
- 吸附后鼠标悬停自动展开。
- 系统托盘菜单。
- 手动刷新额度。
- 自动刷新间隔设置。
- 设置保存到用户配置目录。
- 可自定义颜色：
  - `7d` 进度条颜色。
  - `5h` 进度条颜色。
  - 轨道颜色。
  - 轨道边框颜色。
- 点击颜色预览块即可打开调色盘。
- 进度条文字会根据填充色自动选择更易读的文字颜色。
- 刷新时尽量只更新变化的控件，减少整个 HUD 闪烁。
- GitHub Actions 支持构建 Windows x64 可执行文件。

---

## 显示内容

### 展开态

展开态按 provider 模块化展示，每个 provider 是一个独立卡片。

示例：

```text
Codex                         Updated 20:52
7d   [       69%       ]       R Jul 10 18:39
5h   [       80%       ]       R        01:30

AGY · Gemini                  Checked 20:52
7d   [       97%       ]       R Jul 15 12:45
5h   [       92%       ]       R        22:45
```

说明：

- `Codex` 和 `AGY · Gemini` 分别作为独立模块显示。
- 两者在 UI 中都统一使用 `7d` 与 `5h` 标签。
- AGY 内部实际读取的是 Antigravity 的 `gemini-weekly` 与 `gemini-5h`，其中 `gemini-weekly` 在 UI 中显示为 `7d`。
- 正常状态下不显示 `Source Managed AGY` 这类调试信息。
- 读取失败时，AGY 模块会显示简短错误状态，并在 HUD 附近显示非模态提示。

### 吸附折叠态

折叠态保持极简，只显示 provider 名称和两个额度条。

示例：

```text
Codex   [ 7d ]   [ 5h ]
AGY     [ 7d ]   [ 5h ]
```

规则：

- 折叠态不显示百分比。
- 折叠态不显示 reset 时间。
- AGY 未启用时，只显示 Codex 行。
- AGY 启用但不可用时，可显示 `AGY Offline` 或灰色额度条。

---

## Codex 额度读取

Codex 使用本机 Codex CLI 的 app-server 接口读取额度。

当前读取模式：

```text
每次刷新
  -> 启动 codex app-server --listen stdio://
  -> initialize
  -> account/rateLimits/read
  -> 解析 7d / 5h
  -> 结束本次子进程
```

Codex CLI 路径解析顺序：

1. `CODEX_CLI_PATH` 环境变量。
2. `%LOCALAPPDATA%\OpenAI\Codex\bin\codex.exe`。
3. `%LOCALAPPDATA%\OpenAI\Codex\bin\` 下的版本化子目录。
4. 回退到 `PATH` 中的 `codex`。

如果 Codex 读取失败，请确认：

- Codex CLI 已安装。
- Codex CLI 已登录。
- `codex` 命令可在终端中运行。
- 如自动查找失败，可设置 `CODEX_CLI_PATH`。

示例：

```powershell
$env:CODEX_CLI_PATH = "C:\Path\To\codex.exe"
dotnet run --project .\CodexQuotaHud.csproj
```

---

## AGY / Antigravity 额度读取

AGY 是可选功能，默认关闭。启用后，HUD 使用 **Managed AGY** 模式。

### Managed AGY 是什么

Managed AGY 表示：

```text
HUD 负责启动一个自己托管的 agy.exe
HUD 尝试隐藏该 AGY CLI 窗口
HUD 等待本地 quota endpoint ready
HUD 周期性读取 RetrieveUserQuotaSummary
HUD 退出时只关闭自己启动的 agy.exe
```

它不是每次刷新都启动 / 销毁 AGY，而是复用一个由 HUD 管理的后台 `agy.exe`。

### 为什么不每次刷新都启动 AGY

AGY CLI 是 TUI/CLI，不是专门的 one-shot quota server。首次运行时可能需要：

- 登录。
- trust folder。
- 权限确认。
- CLI 初始化。

因此 AGY 更适合常驻托管，而不是每次刷新都冷启动。

### AGY 依赖条件

需要：

- Windows 上已安装 Antigravity CLI。
- `agy.exe` 可被找到。
- 用户已完成 AGY CLI 的首次登录 / trust folder。

常见 Windows 安装位置：

```text
%LOCALAPPDATA%\agy\bin\agy.exe
```

AGY CLI 安装命令可参考官方文档，通常类似：

```powershell
irm https://antigravity.google/cli/install.ps1 | iex
```

### AGY 读取方式

AGY Provider 会：

1. 启动或复用 HUD 托管的 `agy.exe`。
2. 枚举该进程拥有的本地监听端口。
3. 请求本地 HTTPS endpoint：

```text
POST https://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary
```

4. 解析 `Gemini Models` 中的：

```text
gemini-weekly -> UI 显示为 7d
gemini-5h     -> UI 显示为 5h
```

### AGY 错误提示

如果启用了 AGY 但读取失败，HUD 不应该弹出打断式 MessageBox，而是在 HUD 附近显示非模态提示。

常见提示：

```text
AGY CLI not found. Please install Antigravity CLI.
```

```text
AGY started, but quota endpoint is not ready. Please finish login/trust in AGY.
```

```text
Failed to read AGY quota. Please make sure AGY CLI is installed and signed in.
```

同一种错误不应每次刷新都反复弹出。

### AGY 进程安全

HUD 只能关闭自己启动的 AGY 进程，不能执行 `kill all agy.exe`。

建议记录：

```text
ManagedAgyPid
ManagedAgyStartTime
ManagedAgyExecutablePath
ManagedAgyWorkingDirectory
ManagedAgyPort
ManagedAgyWasStartedByHud
```

退出时只关闭符合记录的托管进程，避免误杀用户自己打开的 AGY CLI。

---

## 设置

设置窗口可从 HUD 右键菜单打开。

设置保存路径：

```text
%APPDATA%\CodexQuotaHud\settings.json
```

典型设置包括：

- 自动刷新间隔。
- 是否启用 Antigravity。
- `7d` 颜色。
- `5h` 颜色。
- 轨道颜色。
- 轨道边框颜色。

推荐默认值：

```text
Auto Refresh:       1 min
Enable Antigravity: false
7d Color:           #4EA1FF
5h Color:           #C2410C
Track Color:        #303740
Track Border Color: #7A8796
```

颜色设置规则：

- Hex 输入框使用 `#RRGGBB` 格式。
- 颜色预览块显示当前颜色。
- 点击颜色预览块打开系统调色盘。
- 删除单独的 `Change` 按钮，保持设置窗口紧凑。
- Reset 只更新表单字段，Save 后才持久化。

---

## 构建和本地运行

从仓库根目录运行：

```powershell
dotnet restore .\CodexQuotaHud.csproj
dotnet build .\CodexQuotaHud.csproj
dotnet run --project .\CodexQuotaHud.csproj
```

如果构建时提示 `CodexQuotaHud.exe` 被占用，通常是旧的 HUD 还在托盘运行。请先从托盘右键 `Exit` 退出，或结束对应进程后再构建。

---

## 本地发布

### 不包含 .NET 的 framework-dependent 版本

```powershell
dotnet publish .\CodexQuotaHud.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true `
  -p:DebugType=none `
  -p:DebugSymbols=false `
  -o .\publish\no-dotnet
```

### 包含 .NET 的 self-contained 版本

```powershell
dotnet publish .\CodexQuotaHud.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:DebugType=none `
  -p:DebugSymbols=false `
  -o .\publish\with-dotnet
```

---

## 下载版本说明

Release assets 可包含两个 exe：

| 文件 | 说明 |
|---|---|
| `CodexQuotaHud-win-x64-no-dotnet.exe` | 体积较小，需要用户已安装 .NET 8 Desktop Runtime x64。 |
| `CodexQuotaHud-win-x64-with-dotnet.exe` | 体积较大，包含 .NET 运行时，不确定时推荐使用。 |

---

## 推荐项目结构

```text
codex-quota-hud/
├─ .github/
│  └─ workflows/
│     └─ build-windows.yml
├─ src/
│  ├─ Program.cs
│  ├─ Models/
│  │  └─ QuotaModels.cs
│  ├─ Providers/
│  │  ├─ IQuotaProvider.cs
│  │  ├─ Codex/
│  │  │  ├─ CodexQuotaProvider.cs
│  │  │  ├─ CodexQuotaReader.cs
│  │  │  └─ CodexQuotaParser.cs
│  │  └─ Antigravity/
│  │     ├─ AgyManagedQuotaProvider.cs
│  │     ├─ AgyEndpointDiscovery.cs
│  │     ├─ AgyProcessManager.cs
│  │     └─ AntigravityQuotaParser.cs
│  ├─ Services/
│  │  ├─ QuotaPoller.cs
│  │  ├─ SettingsStore.cs
│  │  └─ AppSettings.cs
│  └─ UI/
│     ├─ MainHudForm.cs
│     ├─ ProviderCardControl.cs
│     ├─ CollapsedProviderRowControl.cs
│     ├─ QuotaBarControl.cs
│     ├─ SettingsForm.cs
│     └─ HudToastForm.cs
├─ docs/
│  ├─ DEVELOPMENT.md
│  └─ instructions/
├─ .gitignore
├─ CodexQuotaHud.csproj
└─ README.md
```

实际文件名可与当前实现略有差异，但建议长期保持以下边界：

- Provider 负责读取额度。
- Parser 负责解析协议返回。
- Model 负责统一额度数据。
- UI 只负责展示统一模型。
- Settings 只负责配置读取与保存。

---

## GitHub Actions 发布流程

仓库可通过 GitHub Actions 构建 Windows x64 可执行文件。

预期产物：

```text
CodexQuotaHud-win-x64-no-dotnet.exe
CodexQuotaHud-win-x64-with-dotnet.exe
```

建议标签格式统一使用：

```powershell
git tag v0.1.5
git push origin v0.1.5
```

如果 workflow 只监听 `v*` 标签，则 `0.1.5` 这种不带 `v` 的标签不会触发 Release 上传。

---

## 故障排查

### Codex 读取失败

请检查：

- Codex CLI 是否安装。
- Codex CLI 是否登录。
- `codex` 是否能在终端中运行。
- 是否需要设置 `CODEX_CLI_PATH`。

### AGY 读取失败

请检查：

- 设置中是否启用了 Antigravity。
- Antigravity CLI 是否安装。
- `agy.exe` 是否存在。
- 是否已经运行过 AGY 并完成登录 / trust folder。
- HUD 是否有权限启动隐藏 AGY 进程。

### 构建失败，提示 exe 被占用

这是旧的 HUD 进程仍在运行。请先：

1. 在系统托盘找到 Codex Quota HUD。
2. 右键选择 `Exit`。
3. 再运行 `dotnet build`。

如果托盘退出失败，可在管理员 PowerShell 中结束进程。

### AGY 隐藏启动失败

AGY 是 TUI/CLI，首次配置可能无法完全隐藏运行。请手动运行一次：

```powershell
agy
```

完成登录 / trust 后，再回到 HUD 中启用 Antigravity。

---

## 设计原则

- 保持应用小巧。
- 保持 Windows 原生 WinForms，不使用 WebView 重写。
- 保持 Codex、AGY、未来 provider 的读取逻辑模块化。
- UI 只展示统一后的额度模型。
- 不在正常 UI 中显示调试信息。
- 不因单个 provider 失败影响其他 provider 显示。
- 不自动误杀用户自己的外部进程。
- 不记录完整 token、完整 command line、完整 raw JSON 或账号隐私信息。
- 优先做小而可验证的改动。

---

## License

如果本仓库计划公开复用，请添加明确的许可证文件。
