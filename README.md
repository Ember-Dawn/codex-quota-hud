# Codex Quota HUD

A lightweight Windows HUD for viewing local Codex quota usage.

This project is a small native desktop utility built with **C# + WinForms**. It displays the remaining **7-day** and **5-hour** Codex quota in a compact heads-up display, supports top-edge docking, hover expansion, a tray menu, configurable refresh intervals, and configurable bar colors.

## Goals

Codex Quota HUD is intentionally small. It focuses on only three things:

1. Reading Codex quota from the local Codex CLI.
2. Normalizing the quota data into a simple internal model.
3. Showing the result in a lightweight HUD with tray controls.

It does **not** implement pace analysis, historical quota prediction, or usage advice.

## Features

- Native Windows HUD built with WinForms.
- Reads Codex quota through the local Codex CLI app-server interface.
- Shows two quota windows:
  - `7d` quota remaining
  - `5h` quota remaining
- Expanded HUD:
  - title row with latest data update time
  - quota bars with percent text
  - reset time shown with `R`
- Collapsed docked HUD:
  - compact quota bars only
  - `7d` and `5h` labels inside the bars
  - no percent text and no reset time
- Top-edge auto docking.
- Hover-to-expand when docked.
- System tray menu.
- Manual refresh.
- Auto refresh interval setting.
- Custom colors for:
  - `7d` bar
  - `5h` bar
  - track color
  - track border color
- Settings saved in the user profile.
- GitHub Actions build workflow for Windows x64.
- Release workflow can publish two `.exe` files:
  - no-.NET version
  - self-contained version with .NET included

## Requirements

### To run the app

- Windows x64.
- Local Codex CLI installed and logged in.
- For the **no-dotnet** build: .NET 8 Desktop Runtime x64 must be installed.
- For the **with-dotnet** build: no separate .NET runtime is required.

### To develop the app

- Windows.
- .NET 8 SDK or newer compatible SDK.
- Git.

## Download options

Release assets may include two executable builds:

| File | Description |
|---|---|
| `CodexQuotaHud-win-x64-no-dotnet.exe` | Smaller build. Requires .NET 8 Desktop Runtime x64. |
| `CodexQuotaHud-win-x64-with-dotnet.exe` | Larger build. Includes the .NET runtime. Recommended if you are not sure. |

## Build and run locally

From the repository root:

```powershell
dotnet restore .\CodexQuotaHud.csproj
dotnet build .\CodexQuotaHud.csproj
dotnet run --project .\CodexQuotaHud.csproj
```

## Publish locally

### Framework-dependent build, no .NET included

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

### Self-contained build, with .NET included

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

## Settings

Settings are saved under the current user's profile, for example:

```text
%APPDATA%\CodexQuotaHud\settings.json
```

Typical settings include:

- auto refresh interval
- `7d` bar color
- `5h` bar color
- track color
- track border color

The Settings window is available from the right-click menu. It uses an English interface and supports editable hex colors with a small color preview block.

## GitHub Actions release workflow

The repository can build Windows x64 executables automatically through GitHub Actions.

The intended release assets are:

```text
CodexQuotaHud-win-x64-no-dotnet.exe
CodexQuotaHud-win-x64-with-dotnet.exe
```

A common workflow is:

```powershell
git tag 0.1.5
git push origin 0.1.5
```

If the workflow is configured to upload assets for tag builds, the two `.exe` files will be attached to the GitHub Release for that tag.

## Project structure

Recommended structure:

```text
codex-quota-hud/
├─ .github/
│  └─ workflows/
│     └─ build-windows.yml
├─ src/
│  ├─ Program.cs
│  ├─ UI/
│  │  ├─ MainHudForm.cs
│  │  ├─ SettingsForm.cs
│  │  └─ QuotaBarControl.cs
│  ├─ Services/
│  │  ├─ CodexQuotaReader.cs
│  │  ├─ QuotaParser.cs
│  │  ├─ SettingsStore.cs
│  │  └─ AppSettings.cs
│  └─ Models/
│     └─ QuotaModels.cs
├─ docs/
│  ├─ DEVELOPMENT.md
│  └─ instructions/
├─ .gitignore
├─ CodexQuotaHud.csproj
└─ README.md
```

## Troubleshooting

### The HUD shows quota read failure

Check whether Codex CLI is installed and logged in.

If the app cannot find Codex automatically, set the path manually before running:

```powershell
$env:CODEX_CLI_PATH = "C:\Path\To\codex.exe"
dotnet run --project .\CodexQuotaHud.csproj
```

### Release assets are not uploaded

Actions artifacts and Release assets are different.

- Actions artifacts appear under a workflow run.
- Release assets appear under a GitHub Release.

Make sure the workflow contains a release upload step and that the build is triggered by a tag, not only by pushing to `main`.

## Design principles

- Keep the app small.
- Prefer native WinForms behavior over WebView-based UI.
- Do not add pace advice unless it is explicitly requested.
- Keep quota reading, parsing, UI, and settings separated.
- Favor small, easy-to-review changes.

## License

Add a license file if this repository is intended to be public or reused by others.
