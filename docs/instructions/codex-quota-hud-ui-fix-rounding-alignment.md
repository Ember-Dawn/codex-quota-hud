# Codex Task: UI Fixes for CodexQuotaHud

Target project:

```text
E:\github\codex-quota-hud
```

## Goal

Apply a small UI correction pass only. Do not modify quota reading, app-server logic, parser logic, or project architecture.

Current issues to fix:

1. Expanded view shows only `7` and `5`; it must show `7d` and `5h`.
2. Updated time at top-right is too far right; align it with the `HH:mm` reset-time column.
3. Replace reset icon `↻` with `R`.
4. Make `7d`, `5h`, `R`, reset time, and top-right updated time use the same font size and weight.
5. Keep the `Codex Quota` title exactly at its current size and weight. Do not enlarge or shrink it.
6. Make rounded window corners visible in both collapsed and expanded states.
7. Make the progress-bar track border clearly visible.
8. Add a setting for progress-bar track border color.

## Token-saving rules

Do not scan the whole repository.

Read only files likely needed for this task, such as:

```text
MainHudForm.cs
QuotaBarControl.cs
SettingsForm.cs
SettingsStore.cs
AppSettings.cs
QuotaModels.cs
README.md
```

Do not read or edit:

```text
bin/
obj/
.git/
*.user
*.suo
*.log
CodexQuotaReader.cs
QuotaParser.cs
```

Only open `CodexQuotaReader.cs` or `QuotaParser.cs` if the build fails because of a type mismatch caused by this UI change. Otherwise leave quota reading untouched.

## Required UI behavior

### Expanded view layout

Use a stable column layout similar to this:

```text
Codex Quota                         10:56

7d  [          75%          ]  R  Jul 10  18:39
5h  [          22%          ]  R          13:33
```

Requirements:

- The left labels must be `7d` and `5h`, not `7` and `5`.
- The left label column must be wide enough to avoid clipping. Suggested width: 28-34 px.
- Keep the `Codex Quota` title font exactly as it currently is.
- The top-right updated time, `7d` / `5h`, `R`, date text, and time text should use the same font size and same font weight.
- The updated time at top-right should align with the `HH:mm` reset-time column, not with the far right edge of the window.
- Use `R` instead of `↻` before reset time.
- `R` should use the same font size and weight as `7d` / `5h`.
- Keep English UI text only.

### Reset time formatting

Use these rules:

```text
7d reset: R Jul 10 18:39
5h reset: R 13:33
```

- 7d reset time should include English abbreviated month and day, for example `Jul 10`.
- 5h reset time should always show only `HH:mm`, even if it crosses midnight.
- Do not show the word `reset`.
- Do not show the `↻` icon.

### Collapsed view layout

Collapsed state should keep the existing idea:

```text
[        7d        ]   [        5h        ]
```

Requirements:

- Keep `7d` and `5h` inside the progress bars.
- Do not show percentages in collapsed state.
- Do not show reset times in collapsed state.
- Make sure the progress-bar track border is visible in collapsed state.

## Rounded corners

Make the outer HUD window corners visibly rounded in both states:

```text
expanded state
collapsed state
```

Suggested values:

```text
Window corner radius: 10-12 px
Progress bar radius: 7-8 px
```

If the current rounded-corner approach only rounds inner controls but not the actual borderless Form, fix the Form itself, for example by applying a rounded Region. Keep the implementation simple and WinForms-compatible.

Also add a subtle window border if needed so the rounded outline is visible:

```text
Window border default: #2E3640
```

Do not add complex shadows or animations.

## Progress-bar track border

The track border must be visible enough to show the 100% boundary.

Suggested defaults:

```text
Track background: #303740
Track border:     #56606C
Border width:     1 px
```

If the border is too subtle, use a slightly brighter default such as:

```text
#67717E
```

Ensure the fill does not cover the border. Draw order should be:

1. Track background
2. Filled area clipped inside track
3. Track border on top
4. Optional text on top

## Settings change

Add a setting for progress-bar track border color.

If a settings window already exists, add one row:

```text
Track Border Color   [#56606C] [color swatch] [Change]
```

Requirements:

- Setting name in UI: `Track Border Color`
- Use editable hex format: `#RRGGBB`
- Show a small color swatch next to the hex input.
- `Change` should use the existing color picker approach if one already exists.
- Validate hex on Save.
- Save this setting to the existing user settings file under the user directory.
- Suggested JSON field name: `trackBorderColor`
- Default value: `#56606C`
- `Reset Defaults` should restore this value too.

Do not create a large settings redesign. Only add this one setting and wire it to the progress bars.

## Do not change

Do not change:

```text
Codex quota reading logic
Codex app-server logic
Quota parser behavior
Auto refresh behavior
Existing color settings except adding Track Border Color
Existing title font size/weight
Repository structure
Project framework version
```

## Acceptance checklist

After changes:

1. Run:

```powershell
dotnet build
```

2. Launch:

```powershell
dotnet run
```

3. Verify expanded view:

```text
- Shows `7d`, not `7`
- Shows `5h`, not `5`
- Top-right updated time aligns with reset `HH:mm` column
- `R` replaces `↻`
- `7d`, `5h`, `R`, reset time, and updated time share font size/weight
- `Codex Quota` title size/weight is unchanged
- Progress-bar track border is visible
- HUD window corners are visibly rounded
```

4. Verify collapsed view:

```text
- `7d` and `5h` are inside bars
- No percentages
- No reset times
- Track border is visible
- Outer window corners are visibly rounded
```

5. Verify Settings:

```text
- `Track Border Color` appears
- Hex input works
- Color swatch updates
- Change button works if color picker exists
- Save persists to user settings
- Reset Defaults restores #56606C
```
