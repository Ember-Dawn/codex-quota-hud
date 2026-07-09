# Codex Task: Polish HUD UI and Add Minimal Settings

Target project:

```text
E:\github\codex-quota-hud
```

Current app:

- C# WinForms app targeting `net8.0-windows`.
- The app already builds and runs.
- Quota reading has already been changed to use the Codex app-server approach.
- Current HUD has two custom progress bars for `7d` and `5h`.
- Current collapsed and expanded states work, but the UI needs polishing.

This task has two stages:

1. Polish the HUD UI.
2. Add a minimal Settings window.

Do not add unrelated features.

---

## Token-saving rules

Please keep context usage low.

Read only the files needed for this task, likely:

```text
CodexQuotaHud.csproj
Program.cs
MainHudForm.cs
QuotaBarControl.cs
QuotaModels.cs
CodexQuotaReader.cs
Settings-related files if they already exist
README.md
.gitignore
```

Do not read or search these folders unless absolutely necessary:

```text
.git/
bin/
obj/
dist/
release/
node_modules/
.vs/
.zed/
```

Do not inspect the old Electron/Tauri repository. This project is a fresh WinForms implementation.

Do not rewrite the whole project. Prefer small, targeted edits.

After each stage, run:

```powershell
dotnet build
```

If a change is risky, stop and report the issue instead of doing a large rewrite.

---

## Non-goals

Do not implement:

- Pace advice.
- History tracking.
- Settings categories beyond the ones listed here.
- Startup-on-boot.
- Auto update.
- Theme system.
- Complex animations.
- Multi-monitor advanced behavior beyond keeping the Settings window visible.
- Four-side docking. Keep current docking behavior.
- Any Electron, Tauri, WebView, or JavaScript code.

---

# Stage 1: HUD UI polish

## General UI language

The app UI should be English only.

Use these labels:

```text
Codex Quota
Refresh Now
Settings...
Exit
Settings
Auto Refresh
Colors
7d Color
5h Color
Reset Defaults
Cancel
Save
Failed
Loading...
```

## Colors

Use these defaults:

```text
7d bar:       #4EA1FF
5h bar:       #C2410C
Track:        #303740
Track border: #414A55
Background:   #181C22
Text:         #F2F4F8
Muted text:   #B8C0CC
```

The settings feature in Stage 2 will allow changing the 7d and 5h bar colors.

## Rounded HUD window

Add a small rounded outer shape to the HUD window.

Recommended radius:

```text
8 to 10 px
```

Keep it simple. A common WinForms approach is acceptable:

- Set a rounded `Region` for the form.
- Optionally draw a subtle border in `OnPaint`.

Do not add heavy custom rendering or third-party packages.

## Progress bar track boundary

The collapsed state currently makes it hard to see the 100% boundary of each bar.

Use this solution:

```text
Keep the gray track background and add a thin track border.
```

The custom progress bar should draw:

1. Rounded track background.
2. Thin track border.
3. Rounded filled portion.
4. Optional centered text.

Do not use the built-in WinForms `ProgressBar`.

## Collapsed docked state

Keep the current concept:

- `7d` and `5h` labels stay inside their progress bars.
- Do not show percent.
- Do not show reset time.
- Keep the layout compact.
- Use the gray track border so the full 100% width is visible.

Target visual structure:

```text
[     7d     ]   [     5h     ]
```

Each bar should have:

- Fixed width.
- Rounded corners.
- Track background.
- Track border.
- Filled portion.
- Centered label text.

## Expanded state

Use this layout:

```text
Codex Quota                         10:07

7d  [          77%          ]   <reset-icon> Jul 10 18:39
5h  [          33%          ]   <reset-icon> 13:33
```

Where `<reset-icon>` is the Unicode character U+21BB. In C# strings, use:

```csharp
"\u21bb"
```

Rules:

- Top-left: `Codex Quota`.
- Top-right: updated time only, formatted as `HH:mm`.
- Do not show the word `updated`.
- Left side of each row: `7d` or `5h`.
- The bar should contain only the percent text, for example `77%`.
- Do not put `7d` or `5h` inside the bar in expanded state.
- Reset time appears to the right of the bar.
- Do not show the word `reset`.
- Use the reset icon U+21BB before reset time.
- Make the bar shorter than the current version so the reset time fits on the same row.
- Make reset time slightly larger and more readable than the current version.

## Reset time formatting

Format reset times as follows:

- `7d`: always show abbreviated English month and day plus time.
  - Example: `Jul 10 18:39`
- `5h`: always show time only.
  - Example: `13:33`
  - Even if the 5h reset crosses midnight, still show time only.

Use invariant/English culture for month abbreviation so it is always like `Jul`, not localized Chinese month text.

## Recommended sizing

Do not make the expanded HUD too wide.

Suggested size range:

```text
Expanded width: 340 to 390 px
Expanded height: 86 to 110 px
Collapsed width: 270 to 330 px
Collapsed height: 28 to 38 px
```

Exact values can be adjusted if the layout looks better.

Keep the UI compact and readable.

---

# Stage 2: Minimal Settings window

Add a right-click menu item:

```text
Settings...
```

It should be available from:

- The HUD right-click context menu, if a HUD context menu exists.
- The tray menu.

Keep existing items such as:

```text
Refresh Now
Exit
```

If the current menu uses different English names, normalize them to the names above.

## Settings window placement

When `Settings...` is clicked, open a small Settings window near the HUD.

Use this placement rule:

1. Prefer showing the Settings window at the lower-right side of the HUD.
2. If it would go outside the current screen work area, move it to the lower-left side.
3. If it still would not fit vertically, place it above the HUD.
4. Always clamp the final position so the entire Settings window stays inside the current screen working area.

Do not center it on screen unless the HUD position is unavailable.

## Settings window content

The Settings window should be English only.

Minimal layout:

```text
Settings

Auto Refresh
[1 min v]

Colors
7d Color  [#4EA1FF      ] [color swatch]
5h Color  [#C2410C      ] [color swatch]

[Reset Defaults]              [Cancel] [Save]
```

## Auto refresh options

Use exactly these options:

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

Use these numeric values internally:

```text
30 sec  -> 30
1 min   -> 60
5 min   -> 300
10 min  -> 600
20 min  -> 1200
```

Apply the selected interval to the app's auto-refresh timer.

If the app currently has no auto-refresh timer, add a simple WinForms `Timer` that refreshes quota at the selected interval.

Do not add an `Off` option in this task.

## Color settings

Use editable hex text boxes.

Each color row should have:

- A text box for the hex value.
- A small color swatch showing the current hex color.

Accepted format:

```text
#RRGGBB
```

Examples:

```text
#4EA1FF
#C2410C
#22C55E
```

Do not support shorthand values like `#FFF`.
Do not support color names like `blue`.
Do not support alpha values like `#AARRGGBB`.

When the user edits a hex value, update the swatch if the value is valid.

On Save:

- Validate both color fields.
- If invalid, show an English message box such as:

```text
Invalid color value. Please use #RRGGBB format.
```

- Do not save invalid values.

Do not add a complex color picker in this task. Hex input plus swatch is enough.

## Reset Defaults button

Add a button:

```text
Reset Defaults
```

It should reset the form fields to:

```text
Auto Refresh: 1 min
7d Color: #4EA1FF
5h Color: #C2410C
```

Prefer this behavior:

- Clicking `Reset Defaults` changes the fields in the Settings window.
- It does not permanently save until the user clicks `Save`.

## Save and apply behavior

When the user clicks `Save`:

1. Validate settings.
2. Save settings to disk.
3. Apply the new auto-refresh interval immediately.
4. Apply the new bar colors immediately.
5. Close the Settings window.

When the user clicks `Cancel`:

- Discard changes.
- Close the Settings window.

## Settings file location

Save settings in the user's roaming app data directory:

```text
%APPDATA%\CodexQuotaHud\settings.json
```

In C#, use:

```csharp
Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
```

Then combine:

```text
CodexQuotaHud\settings.json
```

Suggested JSON format:

```json
{
  "autoRefreshSeconds": 60,
  "sevenDayColor": "#4EA1FF",
  "fiveHourColor": "#C2410C"
}
```

If the file does not exist, use defaults.
If the file is invalid, fall back to defaults and do not crash.

## Suggested files

Add only if needed:

```text
AppSettings.cs
SettingsStore.cs
SettingsForm.cs
```

Keep these files small and focused.

`AppSettings.cs` should contain app settings and default values.

`SettingsStore.cs` should load/save JSON from `%APPDATA%\CodexQuotaHud\settings.json`.

`SettingsForm.cs` should only handle settings UI.

---

# Acceptance tests

After implementation, run:

```powershell
dotnet build
```

Then run:

```powershell
dotnet run
```

Manually verify:

1. App starts.
2. HUD uses English text only.
3. HUD window has rounded corners.
4. Collapsed state:
   - Shows only two progress bars.
   - `7d` and `5h` labels are inside the bars.
   - No percent text is shown.
   - No reset time is shown.
   - Track border makes the 100% boundary visible.
5. Expanded state:
   - Top-left says `Codex Quota`.
   - Top-right shows only update time, for example `10:07`.
   - `7d` and `5h` labels are left of the bars.
   - Bars show only percent text inside.
   - Right side shows reset icon plus time.
   - 7d reset uses format like `Jul 10 18:39`.
   - 5h reset uses format like `13:33`.
6. Right-click menu has `Settings...`.
7. Settings window opens near the HUD and stays inside the screen work area.
8. Settings window uses English text only.
9. Auto Refresh options are exactly:
   - `30 sec`
   - `1 min`
   - `5 min`
   - `10 min`
   - `20 min`
10. Default auto refresh is `1 min`.
11. Color fields use `#RRGGBB` hex format.
12. Color swatches reflect the hex values.
13. Invalid hex values show an English validation error and are not saved.
14. `Reset Defaults` resets fields to defaults.
15. `Save` writes `%APPDATA%\CodexQuotaHud\settings.json`.
16. Saved colors apply to the HUD immediately.
17. Saved refresh interval applies immediately.
18. App still reads quota correctly.
19. App still builds without errors.

---

# Final report

When done, summarize:

- Files changed.
- Whether `dotnet build` passed.
- Where settings are saved.
- Any known limitations.

Keep the report concise.
