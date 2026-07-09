# Codex Quota HUD

Codex Quota HUD 是一个轻量级 Windows 桌面 HUD 工具，用于在桌面上快速查看本机 AI 工具的额度状态。

当前支持：

- **Codex**：通过本机 Codex CLI 读取 `7d` / `5h` 额度。
- **AGY · Gemini**：可选启用，通过 HUD 托管的 Antigravity CLI `agy.exe` 读取 Gemini `7d` / `5h` 额度。

项目使用 **C# + WinForms** 构建，目标是保持小巧、原生、稳定，不做复杂的额度预测、历史分析或账号仪表盘。

---

## 功能特性

- 原生 Windows HUD，基于 WinForms。
- 支持 Codex 额度显示：
  - `7d` 剩余额度
  - `5h` 剩余额度
  - 重置时间
  - 最近更新时间
- 支持可选 AGY · Gemini 额度显示：
  - `7d`，内部来源为 `gemini-weekly`
  - `5h`，内部来源为 `gemini-5h`
  - 最近检查时间
  - 重置时间
- 保持原有两种 HUD 形态：
  - **展开态**：以模块卡片展示 Codex 和 AGY 的详细信息。
  - **吸附/折叠态**：以紧凑行展示 `Codex [7d] [5h]`、`AGY [7d] [5h]`。
- 支持靠近屏幕顶部自动吸附。
- 吸附后支持鼠标悬停展开。
- 支持系统托盘菜单：
  - Refresh Now
  - Settings
  - Exit
- 支持自动刷新间隔设置。
- 支持颜色自定义：
  - `7d` 进度条颜色
  - `5h` 进度条颜色
  - track 颜色
  - track border 颜色
- 设置界面支持直接编辑 hex 色值。
- 点击颜色预览色块可以打开系统调色盘。
- 刷新时尽量只更新变化的控件，避免整个 HUD 闪烁。
- 退出时会清理 HUD 自己启动的 Managed AGY 进程。

---

## 设计目标

本项目故意保持克制，只关注三件事：

1. 从本机 CLI 或本地 endpoint 读取额度。
2. 将不同来源的额度统一成简单的 provider/bucket 模型。
3. 用轻量 HUD 展示结果，并提供最小必要的托盘和设置能力。

不作为默认目标的功能：

- pace 建议
- 历史额度预测
- 长期使用分析
- 图表仪表盘
- 账号管理
- 云同步
- 自动更新系统
- 跨平台 UI 重写
- Electron / Tauri / WebView 迁移

---

## 运行要求

### 基础要求

- Windows x64。
- Codex CLI 已安装并登录。

### AGY · Gemini 可选功能要求

只有在 Settings 中勾选 **Enable Antigravity** 时才需要：

- Antigravity CLI `agy.exe` 已安装。
- 首次使用前建议手动运行一次 `agy`，完成登录、trust folder 或其他首次交互。
- HUD 会尝试以隐藏方式启动并托管 `agy.exe`，然后读取本地 quota endpoint。

AGY 的安装位置通常类似：

```text
%LOCALAPPDATA%\agy\bin\agy.exe
```

如果隐藏启动后 endpoint 长时间不可用，通常说明 `agy` 还需要用户完成首次登录或 trust。此时请在终端里手动运行一次：

```powershell
agy
```

---

## 下载版本说明

Release assets 通常包含两个 Windows x64 可执行文件：

| 文件 | 说明 |
|---|---|
| `CodexQuotaHud-win-x64-no-dotnet.exe` | 体积较小，需要本机安装 .NET 8 Desktop Runtime x64。 |
| `CodexQuotaHud-win-x64-with-dotnet.exe` | 体积较大，自带 .NET 运行时，推荐不确定环境时使用。 |

---

## 本地构建与运行

在仓库根目录执行：

```powershell
dotnet restore .\CodexQuotaHud.csproj
dotnet build .\CodexQuotaHud.csproj
dotnet run --project .\CodexQuotaHud.csproj
```

如果使用 `dotnet run` 时提示：

```text
CodexQuotaHud.exe is being used by another process
```

说明旧的 HUD 实例还在运行并锁住了输出文件。请先从托盘右键选择 `Exit`，或结束残留进程后再重新运行：

```powershell
Get-Process CodexQuotaHud -ErrorAction SilentlyContinue | Stop-Process -Force
```

如果提示拒绝访问，请使用管理员 PowerShell 执行。

---

## 本地发布

### 不包含 .NET 的框架依赖版本

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

### 包含 .NET 的自包含版本

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

## 设置说明

设置文件保存位置：

```text
%APPDATA%\CodexQuotaHud\settings.json
```

主要设置项：

- Auto Refresh：自动刷新间隔。
- Enable Antigravity：是否启用 AGY · Gemini 额度显示，默认关闭。
- 7d Color：`7d` 进度条颜色。
- 5h Color：`5h` 进度条颜色。
- Track Color：进度条底色。
- Track Border：进度条边框颜色。

默认颜色：

```text
7d Color:           #4EA1FF
5h Color:           #C2410C
Track Color:        #303740
Track Border Color: #7A8796
```

颜色输入框使用 `#RRGGBB` 格式。点击右侧颜色块可以打开调色盘。

---

## AGY · Gemini 工作方式

AGY 功能启用后，HUD 采用 **Managed AGY** 模式：

```text
HUD 启动
  -> 检查 AGY 是否启用
  -> 启动一个 HUD 托管的 agy.exe
  -> 使用专用工作目录
  -> 发现 agy.exe 的本地监听端口
  -> 调用 RetrieveUserQuotaSummary
  -> 解析 Gemini 的 7d / 5h 额度
```

专用工作目录通常为：

```text
%LOCALAPPDATA%\CodexQuotaHud\agy-provider
```

AGY provider 只读取 quota，不发送聊天 prompt，不会向 AGY 会话追加对话内容。

退出 HUD 时，程序只会尝试关闭 **自己启动的** `agy.exe`，不会杀掉用户手动打开的其他 AGY 进程。

如果后台 AGY 意外退出，下一轮刷新会尝试重新启动 Managed AGY，并重新发现 quota endpoint。

---

## 常见问题

### HUD 显示 Codex 读取失败

请检查：

- Codex CLI 是否安装。
- Codex CLI 是否已登录。
- 当前 shell 是否能执行 `codex`。

如果程序无法自动找到 Codex，可设置环境变量：

```powershell
$env:CODEX_CLI_PATH = "C:\Path\To\codex.exe"
dotnet run --project .\CodexQuotaHud.csproj
```

### HUD 显示 AGY Offline / endpoint not ready

请检查：

- 是否已安装 Antigravity CLI。
- 是否可以在终端执行 `agy`。
- 是否已经完成登录、trust folder 或首次交互。

建议手动运行一次：

```powershell
agy
```

完成首次设置后，再重新打开 HUD 或点击 `Refresh Now`。

### 右键 Exit 后程序没有正常退出

如果使用的是旧版本，可能遇到托盘图标消失但 HUD 残留的问题。新版应通过异步退出流程修复：退出时会先停止刷新、取消后台请求、释放 provider，再关闭 HUD 和托盘图标。

如果仍然遇到残留进程，可执行：

```powershell
Get-Process CodexQuotaHud -ErrorAction SilentlyContinue | Stop-Process -Force
```

### dotnet run 无法覆盖 exe

通常是旧实例还在运行。先退出或结束 `CodexQuotaHud.exe` 后再运行。

---

## GitHub Actions 发布流程

仓库可以通过 GitHub Actions 构建 Windows x64 可执行文件。

预期输出：

```text
CodexQuotaHud-win-x64-no-dotnet.exe
CodexQuotaHud-win-x64-with-dotnet.exe
```

如果使用 tag 发布，建议 workflow 同时兼容：

```text
0.1.5
v0.1.5
```

避免只支持 `v*` 导致无 `v` 标签无法上传 Release assets。

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
│  │  ├─ QuotaModels.cs
│  │  └─ ProviderQuotaModels.cs
│  ├─ Providers/
│  │  ├─ IQuotaProvider.cs
│  │  ├─ Codex/
│  │  │  └─ CodexQuotaProvider.cs
│  │  └─ Antigravity/
│  │     ├─ ManagedAgyQuotaProvider.cs
│  │     ├─ ManagedAgyProcess.cs
│  │     ├─ AgyEndpointDiscovery.cs
│  │     └─ AntigravityQuotaParser.cs
│  ├─ Services/
│  │  ├─ CodexQuotaReader.cs
│  │  ├─ QuotaParser.cs
│  │  ├─ QuotaPoller.cs
│  │  ├─ SettingsStore.cs
│  │  └─ AppSettings.cs
│  └─ UI/
│     ├─ MainHudForm.cs
│     ├─ ProviderCardControl.cs
│     ├─ CollapsedProviderRowControl.cs
│     ├─ SettingsForm.cs
│     ├─ HudToastForm.cs
│     └─ QuotaBarControl.cs
├─ docs/
├─ README.md
├─ 项目开发说明.md
└─ CodexQuotaHud.csproj
```

---

## 设计原则

- 保持小工具定位。
- 优先使用 WinForms 原生能力，不引入 WebView 重写。
- Codex 与 AGY 的数据读取方式要模块化。
- UI 只消费统一的 provider snapshot，不直接依赖具体协议字段。
- AGY 是可选功能，默认关闭。
- Managed AGY 只能管理 HUD 自己启动的进程。
- 退出流程必须可靠，不能留下锁定 exe 的残留进程。
- 刷新时尽量局部更新，避免整个窗口闪烁。

---

## License

如果该仓库打算公开复用，请添加明确的 license 文件。
