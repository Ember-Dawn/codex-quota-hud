# Codex Quota HUD Development Guide

This document is written for future maintainers and AI coding agents. It explains what this project does, how it is structured, how the main features work, and how to safely continue development without re-reading unrelated files or accidentally making the project too complex.

## 1. Project summary

Codex Quota HUD is a lightweight Windows desktop utility built with **C# + WinForms**.

Its purpose is to display local Codex quota information in a small HUD:

- `7d` remaining quota
- `5h` remaining quota
- reset times
- latest update time

The app intentionally avoids heavy features such as historical prediction, pace advice, charts, analytics, account dashboards, or Electron/Tauri-style WebView UI.

The core product idea is:

```text
Read local Codex quota -> normalize quota data -> show it in a compact native HUD.
```

## 2. Non-goals

Do not add these unless explicitly requested:

- pace advice
- long-term history analysis
- quota prediction
- complex charts
- account management
- cloud sync
- auto update system
- startup registration
- multi-platform UI framework migration
- Electron, Tauri, Wails, or WebView rewrite

This project should remain a small Windows-native utility.

## 3. Technology stack

- Language: C#
- UI: WinForms
- Target framework: `net8.0-windows`
- Runtime target: Windows x64
- Build: `dotnet build`
- Publish: `dotnet publish`
- CI: GitHub Actions on `windows-latest`

The project uses WinForms because it is simple, native, AI-friendly, and reliable for HUD, tray, topmost windows, custom painting, and Windows positioning behavior.

## 4. Recommended repository layout

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

Keep `CodexQuotaHud.csproj` in the repository root so local commands and GitHub Actions can reference it directly.

## 5. Main modules

### 5.1 `Program.cs`

Application entry point.

Responsibilities:

- initialize WinForms application settings
- load app settings
- start the main HUD form

Avoid putting quota reading, UI layout details, or GitHub Actions logic here.

### 5.2 `MainHudForm.cs`

Main HUD window.

Responsibilities:

- render expanded HUD
- render collapsed docked HUD
- manage docking state
- manage hover-to-expand behavior
- manage tray menu actions
- trigger manual and automatic refresh
- show refresh status in the title row
- apply settings to UI controls

Important UI states:

```text
Floating expanded:
  Normal detailed HUD.

Docked collapsed:
  Compact bar-only HUD near the top edge.

Docked expanded:
  Same detailed layout as floating expanded, shown while mouse is hovering.
```

Do not duplicate two different detailed layouts. The floating expanded view and docked expanded view should share the same visual layout.

### 5.3 `QuotaBarControl.cs`

Custom painted progress bar control.

Responsibilities:

- draw track background
- draw track border
- draw fill area
- draw label or percent text depending on state

Recommended design:

- Track: rounded rectangle.
- Track border: visible border, configurable color.
- Fill: exact width based on percent.
- Low percentage behavior: do not force fill width to the bar height. This avoids the `5% looks the same as 11%` problem.
- Collapsed state: show `7d` or `5h` inside the bar.
- Expanded state: show percent text inside the bar.

Recommended defaults:

```text
7d color:          #4EA1FF
5h color:          #FFB454
Track color:       #303740
Track border:      #7A8796
Background color:  #181C22
Text color:        #F2F4F8
```

### 5.4 `SettingsForm.cs`

Small settings window opened near the HUD.

Responsibilities:

- English-only settings UI
- auto refresh interval selection
- hex color editing
- color preview blocks
- reset defaults button
- save/cancel behavior

Auto refresh options:

```text
30 sec
1 min
5 min
10 min
20 min
```

Default:

```text
1 min
```

Color fields should use `#RRGGBB` format. Keep validation simple and explicit.

### 5.5 `SettingsStore.cs`

Reads and writes app settings.

Recommended settings path:

```text
%APPDATA%\CodexQuotaHud\settings.json
```

Responsibilities:

- create settings directory if needed
- load settings
- fall back to defaults if the file is missing or invalid
- save settings

Do not store user settings in the repository or next to the executable.

### 5.6 `AppSettings.cs`

App settings model.

Typical fields:

```text
AutoRefreshSeconds
SevenDayColor
FiveHourColor
TrackColor
TrackBorderColor
```

Future fields may include:

```text
Window position
Docked state
Always on top
Opacity
```

Add fields conservatively.

### 5.7 `CodexQuotaReader.cs`

Reads quota data from local Codex CLI.

Responsibilities:

- resolve Codex CLI path
- start Codex app-server using stdio
- send app-server requests
- read JSON responses
- return raw quota data or normalized data depending on current implementation

Expected Codex path resolution order:

1. `CODEX_CLI_PATH` environment variable
2. `%LOCALAPPDATA%\OpenAI\Codex\bin\codex.exe`
3. versioned Codex subdirectories under `%LOCALAPPDATA%\OpenAI\Codex\bin\`
4. fallback to `codex` on `PATH`

The intended Codex app-server launch pattern is:

```text
codex app-server --listen stdio://
```

Then send requests for initialization and rate limit reading. The app should not rely on a hypothetical `codex quota` command.

If reading fails, the HUD should show a short status such as `Failed`. Detailed errors should go to a debug log or diagnostic output, not the HUD layout.

### 5.8 `QuotaParser.cs`

Converts raw Codex output or app-server JSON into internal quota models.

Responsibilities:

- parse `7d` quota
- parse `5h` quota
- convert used percent to remaining percent if needed
- convert reset timestamps to local display time
- return safe fallback values on parse failure

Avoid embedding UI formatting in the parser. UI-specific display strings should be produced by the UI layer or a small formatting helper.

### 5.9 `QuotaModels.cs`

Data models for quota state.

Typical concepts:

```text
QuotaSnapshot
QuotaWindow
```

A snapshot should include:

- `7d` window
- `5h` window
- update time
- optional status/error information

A quota window should include:

- remaining percent
- reset time
- display label

## 6. UI specification

### 6.1 Language

The interface should be English-only.

Recommended strings:

```text
Codex Quota
Refreshing...
Updated
Failed
Settings
Auto Refresh
Colors
7d Color
5h Color
Track Color
Track Border Color
Reset Defaults
Cancel
Save
Refresh Now
Exit
```

### 6.2 Expanded HUD

Recommended layout:

```text
Codex Quota        Refreshing...    11:14

7d  [        73%        ]  R  Jul 10  18:39
5h  [        11%        ]  R          13:33
```

Rules:

- `Codex Quota` title keeps the current size and weight.
- Other status text should be readable and bold enough:
  - `7d`
  - `5h`
  - `R`
  - reset date/time
  - top-right update time
  - refresh status
- The top-right update time should align with the reset `HH:mm` time column.
- The refresh status should appear in the title row, to the left of the update time.
- Reset marker is `R`, not `reset` and not `↻`.
- `7d` reset format: `Jul 10 18:39`.
- `5h` reset format: `13:33`, even if it crosses midnight.

### 6.3 Collapsed docked HUD

Recommended layout:

```text
[          7d          ]   [          5h          ]
```

Rules:

- Show only two bars.
- Show `7d` and `5h` inside the bars.
- Do not show percentages.
- Do not show reset times.
- Keep track border visible so users can see the 100% boundary.

### 6.4 Rounded corners

The form itself should have rounded corners. Merely painting a rounded rectangle inside a rectangular form is not enough if the outer window still appears rectangular.

Recommended radii:

```text
Expanded form radius: 14 px or larger if visually needed
Collapsed form radius: 10-12 px
Progress track radius: moderate rounded track
```

## 7. Refresh behavior

The app supports manual and automatic refresh.

Manual refresh can be triggered from the right-click menu.

Auto refresh interval options:

```text
30 sec
1 min
5 min
10 min
20 min
```

Default is `1 min`.

During refresh, title row status should show:

```text
Refreshing...
```

On success, it may briefly show:

```text
Updated
```

On failure, it should show:

```text
Failed
```

Do not place refresh status at the bottom of the HUD. It may be clipped and wastes vertical space.

## 8. Settings behavior

Settings window should open near the HUD.

Positioning rule:

1. Prefer HUD right-bottom side.
2. If there is not enough space, use left-bottom side.
3. If there is not enough space below, place above.
4. Always keep the settings window inside the current screen working area.

Settings should save to:

```text
%APPDATA%\CodexQuotaHud\settings.json
```

Reset Defaults should restore:

```text
Auto Refresh:       1 min
7d Color:           #4EA1FF
5h Color:           #FFB454
Track Color:        #303740
Track Border Color: #7A8796
```

Recommended behavior: Reset Defaults updates the form fields first. Save applies and persists them.

## 9. GitHub Actions

Workflow file:

```text
.github/workflows/build-windows.yml
```

Expected outputs:

```text
CodexQuotaHud-win-x64-no-dotnet.exe
CodexQuotaHud-win-x64-with-dotnet.exe
```

Build order:

1. no-.NET / framework-dependent build
2. with-.NET / self-contained build

The no-.NET build is smaller but requires .NET 8 Desktop Runtime x64.

The with-.NET build is larger but should run without a separate .NET runtime.

Release upload rules:

- Push to `main`: build and upload Actions artifacts.
- Push a tag: build and upload Release assets.

If tags may be named either `0.1.5` or `v0.1.5`, the workflow should support all tags rather than only `v*`.

## 10. Testing checklist

After any code change, run:

```powershell
dotnet build .\CodexQuotaHud.csproj
```

For UI changes, manually test:

- app starts
- expanded HUD is readable
- collapsed HUD is readable
- top docking works
- hover expansion works
- tray menu works
- Refresh Now works
- refresh status appears in title row
- Settings opens near HUD
- settings save and reload correctly
- colors apply immediately or after save as intended
- low quota percentages display accurately and do not become fixed-size dots

For release workflow changes, verify:

- push to `main` creates two Actions artifacts
- tag push creates or updates a Release
- Release contains both `.exe` assets
- no-.NET asset is smaller
- with-.NET asset is larger

## 11. AI development guidance

When using an AI agent to modify this project, keep tasks small.

Good task examples:

```text
Only fix the reset time alignment in MainHudForm.cs.
Do not modify CodexQuotaReader.cs.
Run dotnet build.
```

```text
Only add Track Border Color to SettingsForm and SettingsStore.
Do not change UI layout.
Run dotnet build.
```

```text
Only update GitHub Actions release upload logic.
Do not modify C# files.
```

Avoid broad instructions such as:

```text
Improve the whole app.
Refactor everything.
Make it more modern.
```

They waste context and increase the chance of regressions.

## 12. Files and directories AI should usually ignore

To save context, an AI agent should usually avoid reading:

```text
bin/
obj/
publish/
.git/
.github workflow run logs unless debugging CI
docs/instructions/ old task files unless specifically needed
```

Read first:

```text
README.md
docs/DEVELOPMENT.md
CodexQuotaHud.csproj
src/UI/MainHudForm.cs
src/UI/QuotaBarControl.cs
src/Services/CodexQuotaReader.cs
src/Services/SettingsStore.cs
src/Services/AppSettings.cs
src/Models/QuotaModels.cs
```

## 13. Safe change policy

Before any structural change:

1. Confirm current build passes.
2. Move files without changing logic.
3. Build again.
4. Commit structural changes separately from behavior changes.

Before any quota reader change:

1. Do not alter UI.
2. Add temporary diagnostics if needed.
3. Build.
4. Test with real local Codex CLI.

Before any UI change:

1. Do not alter quota reader.
2. Keep the same public model properties.
3. Build.
4. Manually test expanded and collapsed states.

## 14. Current product direction

The product should stay simple:

```text
A small native Windows HUD for Codex quota.
```

The best next improvements, if requested, are likely:

- better diagnostics when quota reading fails
- optional startup on login
- optional always-on-top toggle
- window position persistence
- simple about dialog with version number
- release notes automation

Avoid turning it into a large quota analytics tool unless the product direction changes.
