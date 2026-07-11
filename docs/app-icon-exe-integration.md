# Windows app icon integration notes

This document records the app icon integration experience for `codex-quota-hud`, including the final working approach and the failed approaches that should not be reintroduced.

## Final working approach

The stable solution is to commit a real Windows `.ico` file to the repository and reference it directly from the WinForms project file.

Current files:

```text
assets/app-icon.svg
assets/app-icon.ico
CodexQuotaHud.csproj
```

Current project configuration:

```xml
<ApplicationIcon>assets\app-icon.ico</ApplicationIcon>
```

Key rule:

```text
Do not generate the application icon during build.
```

The `.ico` file is a source asset for a Windows desktop application, not a temporary build artifact. It should remain committed to Git and should not be ignored by `.gitignore`.

## Asset roles

### `assets/app-icon.svg`

This is the editable source design. It is useful for documentation, README previews, and future redesigns.

### `assets/app-icon.ico`

This is the actual Windows application icon used by the compiler. The `.csproj` file points directly to this file. It should be a valid Windows ICO file and should include common icon sizes such as 16, 32, 48, and 256 px. The current committed icon was generated as a multi-size icon resource.

## Why the committed ICO approach is preferred

Windows executable icons are embedded through Win32 resources. The C# compiler and SDK tooling expect a valid icon resource file at compile time.

A direct committed `.ico` is simple and reliable:

```text
assets/app-icon.ico
  -> <ApplicationIcon>assets\app-icon.ico</ApplicationIcon>
  -> CodexQuotaHud.exe
```

This avoids fragile build-time conversions and keeps the GitHub Actions workflow focused on build and publish.

## Failed approach 1: generating `.ico` with PowerShell during build

The first implementation kept an SVG source and generated an `.ico` file during MSBuild using a PowerShell script:

```text
assets/app-icon.svg
  -> tools/generate-app-icon.ps1
  -> app-icon.ico or obj/.../app-icon.ico
  -> ApplicationIcon
```

The project file contained an MSBuild target similar to:

```xml
<Target Name="GenerateApplicationIcon" BeforeTargets="BeforeBuild;CoreCompile">
  <Exec Command="powershell -NoProfile -ExecutionPolicy Bypass -File &quot;tools\generate-app-icon.ps1&quot; -OutputPath &quot;app-icon.ico&quot;" />
</Target>
```

This failed for multiple reasons.

### Failure A: PowerShell arithmetic parsing

GitHub Actions failed with:

```text
Method invocation failed because [System.Object[]] does not contain a method named 'op_Multiply'.
```

Cause:

PowerShell expressions such as `8 * $scale` were passed inside .NET constructor or method argument lists. On the Windows runner, they were parsed ambiguously and treated as object-array arguments.

Lesson:

Even if a PowerShell GDI+ script works locally, it can fail in CI because of parsing or type coercion differences.

### Failure B: compiler could not find the generated icon

After fixing the PowerShell arithmetic issue, the build failed with:

```text
CSC : error CS7064: Error opening icon file ...\app-icon.ico -- Could not find file ...\app-icon.ico
```

Cause:

The icon was generated under an intermediate path, but the C# compiler tried to open `app-icon.ico` relative to the project root. The generation output path and `ApplicationIcon` path did not match.

Lesson:

Generating an icon during build introduces ordering and path risks. The icon must exist at exactly the path the compiler reads, before the compiler opens it.

### Failure C: generated ICO was not accepted as a Win32 resource

After fixing the path, the build failed with:

```text
CSC : error CS7065: Error building Win32 resources -- Unable to read beyond the end of the stream.
```

Cause:

The compiler found the generated `.ico`, but could not read it as a valid Win32 icon resource. This indicates that the generated ICO stream was malformed or incompatible with the compiler's resource embedding step.

Lesson:

An ICO file can appear to exist and still be invalid for Win32 resource embedding. Manually assembling ICO bytes from PNG streams is error-prone.

## Failed approach 2: committing text/base64 or relying on generated binary files

A tempting workaround is to store `.ico.b64` as text and decode it during build. This has the same core weakness as dynamic generation:

```text
app-icon.ico.b64
  -> decode during build
  -> app-icon.ico
  -> ApplicationIcon
```

This can still fail if the decoded ICO is invalid, if the generation step runs too late, or if the final publish step does not use the generated icon as expected.

For this project, do not use `.ico.b64` or any build-time icon decoding pipeline.

## Failed approach 3: only keeping SVG or README artwork

Keeping only `assets/app-icon.svg` is not enough for a Windows executable icon.

SVG is useful as an editable source asset, but this does not automatically affect:

```text
exe file icon
Windows Explorer icon
Task Manager icon
shortcut icon
WinForms application icon
```

The Windows build needs a real `.ico` file referenced by `ApplicationIcon`.

## Current recommended structure

Keep this structure:

```text
assets/
  app-icon.svg
  app-icon.ico

CodexQuotaHud.csproj
```

Keep this configuration:

```xml
<ApplicationIcon>assets\app-icon.ico</ApplicationIcon>
```

Do not restore:

```text
tools/generate-app-icon.ps1
GenerateApplicationIcon MSBuild target
app-icon.ico.b64 dynamic decoding
```

## Validation checklist

After changing the icon, validate all of the following:

1. `assets/app-icon.ico` exists in the repository.
2. The ICO file is not ignored by `.gitignore`.
3. `CodexQuotaHud.csproj` contains:

   ```xml
   <ApplicationIcon>assets\app-icon.ico</ApplicationIcon>
   ```

4. There is no build-time icon generation target.
5. GitHub Actions build succeeds.
6. The published `CodexQuotaHud-win-x64-no-dotnet.exe` shows the new icon.
7. The tray icon and any visible window icon are checked separately, because embedding the exe icon does not automatically guarantee every runtime `NotifyIcon` or form icon uses it.

## Optional runtime icon follow-up

The current committed ICO approach embeds the icon into the executable. If the app still shows the default icon in the tray or in a visible window, the runtime UI code may also need to load the application icon explicitly.

For example, a future improvement could replace default system icons such as:

```csharp
Icon = SystemIcons.Application
```

with an icon loaded from the executable or embedded resource. This is separate from compiling the icon into the exe.

## GitHub Actions and release notes

The workflow now only builds the smaller framework-dependent Windows executable:

```text
CodexQuotaHud-win-x64-no-dotnet.exe
```

When validating release assets, do not only rerun an old tag if the tag points to an older commit. Create a new tag after the icon fix is on `main`, then download the newly produced release asset.

## Windows icon cache

Windows Explorer and taskbar icon caching can make an old icon appear even after the exe has been updated.

When checking the result:

```text
1. Use a newly downloaded exe.
2. Put it in a new folder.
3. Avoid old shortcuts.
4. Rename the exe if needed.
5. Restart Explorer or Windows if the cache persists.
```

If the executable itself has the new icon but Explorer shows the old one, this is likely cache. If the executable resource still contains the old/default icon, the build did not embed the intended icon.

## Final rule

For this project, the stable rule is:

```text
Use a real, valid, committed Windows .ico file and reference it directly from .csproj.
```

Do not reintroduce dynamic icon generation during build unless there is a strong reason and a dedicated test proves that the resulting ICO is accepted by the C# compiler and Win32 resource embedding step.
