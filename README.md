# Codex Quota HUD

A lightweight Windows HUD for Codex quota.

Status: MVP / experimental

## Features

- Quota reading
- Quota normalization
- Lightweight HUD
- System tray
- Top docking
- Expand on hover while docked

## Run

```powershell
dotnet run
```

## Publish

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

## Codex CLI path

Quota is read through `codex app-server --listen stdio://` and `account/rateLimits/read`.

The app first checks the bundled Codex location under `%LOCALAPPDATA%\OpenAI\Codex\bin`, then falls back to `codex` from `PATH`.

Override the executable path with:

```powershell
$env:CODEX_CLI_PATH = "C:\\path\\to\\codex.exe"
```

## Settings

Settings are saved to `%APPDATA%\CodexQuotaHud\settings.json`.

Current settings:

- Auto refresh interval
- 7d bar color
- 5h bar color
- Progress-bar track color
- Progress-bar track border color

## Not included yet

- Pace judgment
- History analysis

## GitHub Actions Build

This repository includes a Windows build workflow.

On every push to `main`, pull request to `main`, or manual workflow dispatch, GitHub Actions will:

1. restore dependencies,
2. build the project,
3. publish a self-contained Windows x64 single-file build,
4. upload `CodexQuotaHud-win-x64.zip` as a workflow artifact.

To download a build:

1. open the repository on GitHub,
2. go to **Actions**,
3. open the latest **Build Windows** run,
4. download the `CodexQuotaHud-win-x64` artifact.
