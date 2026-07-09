using System.Diagnostics;
using System.Text;

namespace CodexQuotaHud;

public sealed class ManagedAgyProcess : IAsyncDisposable
{
    private const string NotFoundMessage = "AGY CLI not found. Please install Antigravity CLI and run agy once to finish setup.";

    private Process? _process;
    private Task? _stdoutDrainTask;
    private Task? _stderrDrainTask;
    private string? _executablePath;
    private string? _workingDirectory;
    private DateTime? _startTime;
    private bool _wasStartedByHud;
    private AppSettings _settings;

    public ManagedAgyProcess(AppSettings settings)
    {
        _settings = settings.Clone();
    }

    public int? ProcessId => _process is { HasExited: false } ? _process.Id : null;
    public bool IsRunning => _process is { HasExited: false };
    public DateTime? StartTime => _startTime;
    public string? ExecutablePath => _executablePath;
    public string? WorkingDirectory => _workingDirectory;
    public bool WasStartedByHud => _wasStartedByHud;

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings.Clone();
    }

    public async Task<AgyProcessStartResult> EnsureRunningAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_process is { HasExited: false })
        {
            return AgyProcessStartResult.Running(_process.Id, wasStartedNow: false);
        }

        await DisposeExistingProcessObjectAsync();

        cancellationToken.ThrowIfCancellationRequested();
        var agyPath = ResolveAgyPath(_settings.AgyExecutablePath);
        if (agyPath is null)
        {
            return AgyProcessStartResult.Failed(NotFoundMessage);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var workingDirectory = Path.Combine(localAppData, "CodexQuotaHud", "agy-provider");
        Directory.CreateDirectory(workingDirectory);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = agyPath,
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = workingDirectory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };

        try
        {
            DebugLogger.Log("[AGY-DIAG] ensure running");
            DebugLogger.Log($"[AGY-DIAG] agy path={agyPath}");
            DebugLogger.Log($"[AGY-DIAG] working dir={workingDirectory}");
            DebugLogger.Log($"[AGY-DIAG] start hidden=True");
            DebugLogger.Log($"[AGY-DIAG] CreateNoWindow={process.StartInfo.CreateNoWindow} WindowStyle={process.StartInfo.WindowStyle}");
            ProcessDiagnostics.LogSnapshot("agy", "before-start", reason: "before-agy-start");

            cancellationToken.ThrowIfCancellationRequested();
            process.Start();
            DebugLogger.Log($"[AGY-DIAG] agy managed process started pid={process.Id}");
            ProcessDiagnostics.LogSnapshot("agy", "after-start", process.Id, "immediately-after-agy-start");
            _ = Task.Run(() => ProcessDiagnostics.MonitorAsync(
                provider: "agy",
                phase: "monitor",
                targetPid: process.Id,
                duration: TimeSpan.FromSeconds(30),
                interval: TimeSpan.FromMilliseconds(500),
                cancellationToken: CancellationToken.None,
                reason: "after-agy-start"));

            _process = process;
            _executablePath = agyPath;
            _workingDirectory = workingDirectory;
            _startTime = SafeReadStartTime(process);
            _wasStartedByHud = true;
            _stdoutDrainTask = DrainAsync(process.StandardOutput, "[AGY-STDOUT]", cancellationToken);
            _stderrDrainTask = DrainAsync(process.StandardError, "[AGY-STDERR]", cancellationToken);
            return AgyProcessStartResult.Running(process.Id, wasStartedNow: true);
        }
        catch (OperationCanceledException)
        {
            process.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            process.Dispose();
            DebugLogger.Log($"[AGY-DIAG] agy start failed: {Shorten(ex.Message)}");
            return AgyProcessStartResult.Failed("AGY started, but quota endpoint is not ready. Please run agy once manually to finish login/trust.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_settings.CloseManagedAgyOnExit)
        {
            await ShutdownIfOwnedAsync().ConfigureAwait(false);
        }

        await DisposeExistingProcessObjectAsync().ConfigureAwait(false);
    }

    public async Task ShutdownIfOwnedAsync()
    {
        var process = _process;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited && IsSameManagedProcess(process))
            {
                var pid = process.Id;
                DebugLogger.Log($"[AGY-DIAG] stopping managed agy pid={pid}");
                ProcessDiagnostics.LogSnapshot("agy", "before-shutdown", pid, "stopping-managed-agy");
                process.Kill(entireProcessTree: true);
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    DebugLogger.Log($"[AGY-DIAG] stopped managed agy pid={pid}");
                    ProcessDiagnostics.LogSnapshot("agy", "after-shutdown", pid, "stopped-managed-agy");
                }
                catch (TimeoutException)
                {
                    DebugLogger.Log($"[AGY-DIAG] stop timeout pid={pid}");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[AGY-DIAG] agy stop failed: {Shorten(ex.Message)}");
        }
    }

    private async Task DisposeExistingProcessObjectAsync()
    {
        var process = _process;
        _process = null;

        try
        {
            if (_stdoutDrainTask is not null)
            {
                await _stdoutDrainTask.WaitAsync(TimeSpan.FromMilliseconds(250));
            }
        }
        catch
        {
        }

        try
        {
            if (_stderrDrainTask is not null)
            {
                await _stderrDrainTask.WaitAsync(TimeSpan.FromMilliseconds(250));
            }
        }
        catch
        {
        }

        _stdoutDrainTask = null;
        _stderrDrainTask = null;
        process?.Dispose();
        _wasStartedByHud = false;
    }

    private bool IsSameManagedProcess(Process process)
    {
        if (!WasStartedByHud)
        {
            return false;
        }

        try
        {
            if (!string.Equals(process.ProcessName, "agy", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        var startTimeMatches = true;
        if (_startTime.HasValue)
        {
            try
            {
                startTimeMatches = Math.Abs((process.StartTime - _startTime.Value).TotalSeconds) < 2;
            }
            catch
            {
                startTimeMatches = false;
            }
        }

        var pathMatches = true;
        if (!string.IsNullOrWhiteSpace(_executablePath))
        {
            try
            {
                pathMatches = string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? string.Empty), Path.GetFullPath(_executablePath), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                pathMatches = startTimeMatches;
            }
        }

        return startTimeMatches && pathMatches;
    }

    private static async Task DrainAsync(StreamReader reader, string prefix, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                var sanitized = ProcessDiagnostics.SanitizeForLog(line, 300);
                if (!string.IsNullOrWhiteSpace(sanitized) && sanitized != "?")
                {
                    DebugLogger.Log($"{prefix} {sanitized}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static DateTime? SafeReadStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveAgyPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var localPath = Path.Combine(localAppData, "agy", "bin", "agy.exe");
        if (File.Exists(localPath))
        {
            return localPath;
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "agy.exe");
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
            }
        }

        return null;
    }



    private static string Shorten(string text)
    {
        text = text.Trim().ReplaceLineEndings(" ");
        return text.Length <= 180 ? text : text[..180] + "...";
    }
}

public sealed record AgyProcessStartResult(bool Success, int? ProcessId, bool WasStartedNow, string? ErrorMessage)
{
    public static AgyProcessStartResult Running(int processId, bool wasStartedNow) => new(true, processId, wasStartedNow, null);
    public static AgyProcessStartResult Failed(string errorMessage) => new(false, null, false, errorMessage);
}
