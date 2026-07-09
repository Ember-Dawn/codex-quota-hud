# Codex Task: Implement Session-Based Diagnostic Logging

## 0. Project context

Repository:

```text
E:\github\codex-quota-hud
```

This project is a Windows WinForms HUD for displaying Codex and optional Antigravity quota information.

The project currently has diagnostic logging for Codex / AGY startup and process monitoring. The current logging behavior should be improved so logs do not grow forever and are easier to inspect per app launch.

## 1. Goal

Implement a simple session-based diagnostic logging system:

```text
Each app launch creates one new log file.
The log file is written only during that app session.
The log file has a maximum size.
Old log files are cleaned up automatically.
A Settings checkbox controls detailed diagnostic logging.
```

This should replace scattered direct writes such as `File.AppendAllText(..., "debug.log", ...)` for diagnostic logs.

## 2. Required behavior

### 2.1 Log directory

All diagnostic logs must be written under:

```text
%LOCALAPPDATA%\CodexQuotaHud\logs\
```

Example:

```text
C:\Users\Cyan\AppData\Local\CodexQuotaHud\logs\
```

Create the directory automatically if it does not exist.

Do not write diagnostic logs to the application working directory unless the LocalAppData path is unavailable. If a fallback is absolutely necessary, fail silently or use a safe fallback under the current directory, but prefer LocalAppData.

### 2.2 One log per app launch

Each app launch should create a new session log file named:

```text
debug-YYYY-MM-DD_HH-mm-ss.log
```

Example:

```text
debug-2026-07-09_17-28-41.log
```

Do not include PID in the file name.

The first few lines of the log should include session metadata, including at least:

```text
[APP] session started pid=<current-process-id>
[APP] log file=<full-log-path>
[APP] diagnostic logging enabled=<true/false>
```

If an application version is easy to read, also include it:

```text
[APP] version=<version-or-unknown>
```

### 2.3 No `latest.log`

Do not implement `latest.log` for now.

Reason: it is not necessary for this small tool, and it adds extra complexity. Users can sort the `logs` folder by modified date and open the newest `debug-*.log` file.

### 2.4 Maximum size per session log

Each session log has a hard size cap:

```text
2 MB
```

When the current session log reaches 2 MB:

1. Write one final line if possible:

```text
[LOG] Session log reached 2 MB limit. Further diagnostic entries are suppressed.
```

2. Stop writing any further diagnostic entries for the current session.

Do not create part files.
Do not rotate a single session into `part2`, `part3`, etc.
Do not keep appending after the 2 MB limit.

### 2.5 Maximum number of session logs

Keep only the newest 30 session log files:

```text
debug-*.log
```

On app startup, after creating or initializing the log service, scan the log directory and delete older session logs beyond the newest 30.

Sorting rule:

1. Prefer file `LastWriteTimeUtc`.
2. If needed, file name timestamp can be used as a secondary sorting signal.

Do not delete unrelated files in the log directory.
Only delete files matching:

```text
debug-*.log
```

### 2.6 Settings checkbox

Add a Settings checkbox:

```text
Enable diagnostic logging
```

Default value:

```text
true
```

Add a property to `AppSettings`, for example:

```csharp
public bool EnableDiagnosticLogging { get; set; } = true;
```

Persist it through the existing settings store.

The checkbox should appear in the Settings form near the existing general settings, for example:

```text
Auto Refresh
Enable Antigravity
Enable diagnostic logging
Colors
```

### 2.7 Meaning of the setting

When `Enable diagnostic logging = true`, write detailed diagnostic logs, including:

```text
Codex process start/stop
Codex child process monitor
AGY process start/stop
AGY child process monitor
Git / cmd / conhost / node / pwsh process snapshots
netstat discovery
endpoint discovery
quota provider status changes
exit lifecycle diagnostics
```

When `Enable diagnostic logging = false`, suppress detailed diagnostics.

It is acceptable to still write very minimal critical lifecycle/error messages if needed, for example:

```text
[APP] session started
[APP] session ended
[ERROR] unhandled exception
```

But do not write verbose process snapshots when the checkbox is off.

## 3. Architecture requirement

### 3.1 Add a central logger

Create a central logging service/class. Suggested names:

```text
AppLogger
DiagnosticLogger
SessionLogger
```

Recommended capabilities:

```csharp
Initialize(AppSettings settings)
ApplySettings(AppSettings settings)
Info(string category, string message)
Diagnostic(string category, string message)
Error(string category, string message, Exception? ex = null)
Shutdown()
```

The exact API is flexible, but the project should stop using scattered direct `File.AppendAllText` calls for diagnostics.

### 3.2 Thread safety

The logger may be called from async provider tasks, timers, UI events, and process-monitoring tasks.

Make logging thread-safe.

A simple `lock` around file write and size-limit checks is acceptable.

### 3.3 Failure behavior

Logging must never crash the HUD.

If logging fails due to file lock, access denied, invalid path, or IO error:

```text
Swallow the logging exception.
Disable further logging if necessary.
Do not show a user-facing error popup.
Do not break quota refresh.
```

## 4. Replace existing direct log calls

Search the codebase for direct logging calls, especially patterns like:

```csharp
File.AppendAllText(... "debug.log" ...)
AppendLog(...)
```

Replace them with the central logger.

Likely areas include:

```text
CodexQuotaReader / CodexQuotaProvider
ManagedAgyProcess
ManagedAgyQuotaProvider
AgyEndpointDiscovery
ProcessDiagnostics
MainHudForm exit lifecycle diagnostics
```

The exact file names may differ. Search the current source tree.

## 5. Preserve current diagnostic purpose

Do not remove the current diagnostic value.

The new logging system must still help identify whether `git.exe`, `cmd.exe`, `conhost.exe`, `node.exe`, or other child processes are descendants of Codex or AGY.

Keep entries clear and consistently tagged, for example:

```text
[CODEX-DIAG] ...
[AGY-DIAG] ...
[PROCESS-DIAG] provider=codex reason=after-codex-start ...
[PROCESS-DIAG] provider=agy reason=after-agy-start ...
```

## 6. Sensitive information rules

Do not log:

```text
access tokens
refresh tokens
cookies
authorization headers
CSRF tokens
full environment variable dumps
private key material
```

Command lines for local processes are acceptable for diagnostics, but avoid logging anything that looks like a token or secret. If a command line contains obvious token-like fields, redact them.

Use simple redaction patterns where practical, for example:

```text
token=***
csrf=***
authorization=***
```

## 7. UI details

### 7.1 Settings layout

Add the checkbox without making the Settings form cramped.

If needed, increase the form height slightly.

Keep the existing style simple and consistent with the current Settings UI.

### 7.2 No extra buttons required

Do not add an “Open logs folder” button unless it is trivial and does not complicate layout.

The core requirement is the checkbox and session-based logging behavior.

## 8. Tests to run

Please run:

```powershell
dotnet build
```

Then manually test:

```powershell
dotnet run
```

### 8.1 Startup log test

1. Launch the app.
2. Confirm a new file appears under:

```text
%LOCALAPPDATA%\CodexQuotaHud\logs\
```

3. Confirm file name format:

```text
debug-YYYY-MM-DD_HH-mm-ss.log
```

4. Confirm the first lines include the app PID.

### 8.2 Multiple launch test

1. Exit the app normally.
2. Launch again.
3. Confirm a second new log file is created.
4. Confirm the previous log file is not appended.

### 8.3 Settings test

1. Open Settings.
2. Confirm `Enable diagnostic logging` exists and is checked by default.
3. Uncheck it and save.
4. Restart the app.
5. Confirm verbose process diagnostics are no longer written.
6. Re-check it and save.
7. Restart the app.
8. Confirm verbose diagnostics return.

### 8.4 Size cap test

Use a temporary smaller size limit or a test helper if needed.

Confirm that once the session log reaches the limit:

```text
[LOG] Session log reached 2 MB limit. Further diagnostic entries are suppressed.
```

is written once, and no further diagnostic entries are appended.

### 8.5 Retention test

Use a temporary smaller retention count or create fake `debug-*.log` files.

Confirm only the newest 30 matching files are retained.

Do not delete unrelated files.

## 9. Non-goals

Do not implement these in this task:

```text
latest.log
log part files
complex rolling log framework
external logging package unless already used
Attach Only AGY mode
App Local Provider
major UI redesign
```

## 10. Expected result

After this task:

```text
- Each HUD launch produces one clean session log.
- Logs are stored under LocalAppData.
- Logs do not grow beyond 2 MB per session.
- The logs folder keeps only the newest 30 session logs.
- Detailed diagnostic logging can be toggled from Settings and is on by default.
- Existing Codex/AGY process diagnostics continue to work through the central logger.
```
