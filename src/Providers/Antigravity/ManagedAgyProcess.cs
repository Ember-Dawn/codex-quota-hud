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
                CreateNoWindow = _settings.StartAgyHidden,
                WindowStyle = _settings.StartAgyHidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
                WorkingDirectory = workingDirectory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            process.Start();
            _process = process;
            _executablePath = agyPath;
            _workingDirectory = workingDirectory;
            _startTime = SafeReadStartTime(process);
            _wasStartedByHud = true;
            _stdoutDrainTask = DrainAsync(process.StandardOutput, cancellationToken);
            _stderrDrainTask = DrainAsync(process.StandardError, cancellationToken);
            AppendLog($"agy managed process started pid={process.Id}");
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
            AppendLog($"agy start failed: {Shorten(ex.Message)}");
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
                AppendLog($"agy managed process stopping pid={pid}");
                process.Kill(entireProcessTree: true);
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    AppendLog($"agy managed process stopped pid={pid}");
                }
                catch (TimeoutException)
                {
                    AppendLog($"agy managed process stop timeout pid={pid}");
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"agy stop failed: {Shorten(ex.Message)}");
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

    private static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
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

    private static void AppendLog(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(Environment.CurrentDirectory, "debug.log"), line, Encoding.UTF8);
        }
        catch
        {
        }
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
