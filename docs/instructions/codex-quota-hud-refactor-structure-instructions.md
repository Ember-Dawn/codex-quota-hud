# Codex Task: Refactor Project File Structure Only

Repository path:

```powershell
E:\github\codex-quota-hud
```

## Goal

Refactor the project file layout so the repository root is cleaner.

This task is **structure-only**. Do not change application behavior, UI behavior, quota reading behavior, settings behavior, GitHub Actions behavior, or release behavior.

The project currently has too many `.cs` and instruction `.md` files in the repository root. Move source files into `src/` and historical instruction documents into `docs/instructions/`.

## Important constraints

1. Keep `CodexQuotaHud.csproj` in the repository root.
2. Keep `.github/workflows/build-windows.yml` where it is.
3. Keep `.gitignore` in the repository root.
4. Keep `README.md` in the repository root.
5. Do **not** rewrite business logic.
6. Do **not** rewrite UI logic.
7. Do **not** change quota reader logic.
8. Do **not** change GitHub Actions publish/release logic.
9. Do **not** rename the project.
10. Do **not** introduce new dependencies.
11. Do **not** create a complex architecture.
12. Run `dotnet build` after moving files and fix only compile errors caused by the move.

## Desired final structure

Use this lightweight structure:

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
│  └─ instructions/
│     └─ historical instruction markdown files
├─ .gitignore
├─ CodexQuotaHud.csproj
├─ README.md
└─ LICENSE              # only if already present
```

## File move plan

Move these files exactly as follows if they exist:

```text
Program.cs             -> src/Program.cs
MainHudForm.cs         -> src/UI/MainHudForm.cs
SettingsForm.cs        -> src/UI/SettingsForm.cs
QuotaBarControl.cs     -> src/UI/QuotaBarControl.cs
CodexQuotaReader.cs    -> src/Services/CodexQuotaReader.cs
QuotaParser.cs         -> src/Services/QuotaParser.cs
SettingsStore.cs       -> src/Services/SettingsStore.cs
AppSettings.cs         -> src/Services/AppSettings.cs
QuotaModels.cs         -> src/Models/QuotaModels.cs
```

Move historical Codex instruction `.md` files from the repository root into:

```text
docs/instructions/
```

Examples of instruction files to move:

```text
codex-quota-hud-*.md
*-instructions.md
```

Do not move `README.md`.
Do not move workflow files.
Do not move `.gitignore`.
Do not move `.csproj`.

If there is an existing folder containing previous instruction documents, move those documents into `docs/instructions/` too, then remove the old folder only if it becomes empty.

## Namespace guidance

Prefer the minimal safe approach:

- Do **not** change namespaces unless required by compilation.
- If all files currently use the same namespace, keep that namespace.
- Avoid splitting into `CodexQuotaHud.UI`, `CodexQuotaHud.Services`, etc. in this task.

The goal is a clean folder structure, not a namespace redesign.

## `.csproj` guidance

Because this should be an SDK-style C# project, source files under `src/**/*.cs` should normally be included automatically.

Do not manually add many `<Compile Include=...>` entries unless the build fails and the project is not automatically including files.

If the build fails because of source discovery, make the smallest possible `.csproj` change to include:

```xml
<Compile Include="src\**\*.cs" />
```

Only add that if necessary.

## GitHub Actions guidance

Do not change `.github/workflows/build-windows.yml` unless the build proves it is necessary.

The workflow should still call the root project file:

```powershell
dotnet restore .\CodexQuotaHud.csproj
dotnet build .\CodexQuotaHud.csproj -c Release
dotnet publish .\CodexQuotaHud.csproj ...
```

Since the `.csproj` remains in the repository root, the workflow should usually keep working unchanged.

## Validation steps

After moving files, run:

```powershell
dotnet build
```

Then run a basic local launch check if reasonable:

```powershell
dotnet run
```

If `dotnet run` launches the GUI and blocks the terminal, that is expected. Do not treat that as a failure.

## What to report back

After finishing, report:

1. Which files were moved.
2. Whether namespaces were changed. Preferably: "No namespace changes were needed."
3. Whether `CodexQuotaHud.csproj` was changed.
4. Whether `.github/workflows/build-windows.yml` was changed.
5. Result of `dotnet build`.
6. Any remaining risk or warning.

## Acceptance criteria

The task is complete only if:

- Source files are under `src/`.
- UI files are under `src/UI/`.
- service/storage/parser files are under `src/Services/`.
- model files are under `src/Models/`.
- historical instruction markdown files are under `docs/instructions/`.
- repository root is cleaner.
- `CodexQuotaHud.csproj` remains in the root.
- `.github/workflows/build-windows.yml` remains unchanged unless strictly necessary.
- `dotnet build` succeeds.

