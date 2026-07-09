using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace CodexQuotaHud;

internal static class DebugLogger
{
    private const int MaxLineLength = 1000;
    private const long MaxSessionLogBytes = 2 * 1024 * 1024;
    private const int MaxSessionLogs = 15;

    private static readonly object SyncRoot = new();
    private static string? _logPath;
    private static bool _initialized;
    private static bool _diagnosticLoggingEnabled = true;
    private static bool _sizeLimitReached;
    private static bool _loggingFailed;

    public static bool IsDiagnosticLoggingEnabled
    {
        get
        {
            lock (SyncRoot)
            {
                EnsureInitializedLocked(AppSettings.Default());
                return _diagnosticLoggingEnabled && !_sizeLimitReached && !_loggingFailed;
            }
        }
    }

    public static void Initialize(AppSettings settings)
    {
        lock (SyncRoot)
        {
            if (_initialized)
            {
                ApplySettingsLocked(settings);
                return;
            }

            EnsureInitializedLocked(settings);
        }
    }

    public static void ApplySettings(AppSettings settings)
    {
        lock (SyncRoot)
        {
            EnsureInitializedLocked(settings);
            ApplySettingsLocked(settings);
            WriteLineLocked($"[APP] diagnostic logging enabled={_diagnosticLoggingEnabled}", force: true);
        }
    }

    public static void Info(string category, string message)
    {
        Write($"[{SanitizeToken(category)}] {message}", force: true);
    }

    public static void Diagnostic(string category, string message)
    {
        Write($"[{SanitizeToken(category)}] {message}", force: false);
    }

    public static void Error(string category, string message, Exception? ex = null)
    {
        var detail = ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}";
        Write($"[{SanitizeToken(category)}] {detail}", force: true);
    }

    public static void Log(string message)
    {
        Write(message, force: false);
    }

    public static void LogException(string prefix, Exception ex)
    {
        Write($"{prefix}: {ex.GetType().Name}: {ex.Message}", force: false);
    }

    public static void Shutdown()
    {
        lock (SyncRoot)
        {
            if (!_initialized)
            {
                return;
            }

            WriteLineLocked($"[APP] session ended pid={Environment.ProcessId}", force: true);
        }
    }

    private static void Write(string message, bool force)
    {
        lock (SyncRoot)
        {
            EnsureInitializedLocked(AppSettings.Default());
            WriteLineLocked(message, force);
        }
    }

    private static void EnsureInitializedLocked(AppSettings settings)
    {
        if (_initialized)
        {
            return;
        }

        _diagnosticLoggingEnabled = settings.EnableDiagnosticLogging;
        _logPath = CreateSessionLogPath();
        _initialized = true;

        CleanupOldSessionLogs();
        WriteLineLocked($"[APP] session started pid={Environment.ProcessId}", force: true);
        WriteLineLocked($"[APP] log file={_logPath ?? "?"}", force: true);
        WriteLineLocked($"[APP] diagnostic logging enabled={_diagnosticLoggingEnabled}", force: true);
        WriteLineLocked($"[APP] version={GetVersion()}", force: true);
    }

    private static void ApplySettingsLocked(AppSettings settings)
    {
        _diagnosticLoggingEnabled = settings.EnableDiagnosticLogging;
    }

    private static void WriteLineLocked(string message, bool force)
    {
        if (_loggingFailed || _logPath is null)
        {
            return;
        }

        if (!force && !_diagnosticLoggingEnabled)
        {
            return;
        }

        if (_sizeLimitReached)
        {
            return;
        }

        try
        {
            var sanitizedMessage = SanitizeLogMessage(message);
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {sanitizedMessage}{Environment.NewLine}";
            var encoding = Encoding.UTF8;
            var projectedBytes = File.Exists(_logPath) ? new FileInfo(_logPath).Length + encoding.GetByteCount(line) : encoding.GetByteCount(line);
            if (projectedBytes > MaxSessionLogBytes)
            {
                var finalLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [LOG] Session log reached 2 MB limit. Further log entries are suppressed.{Environment.NewLine}";
                var currentBytes = File.Exists(_logPath) ? new FileInfo(_logPath).Length : 0;
                if (currentBytes + encoding.GetByteCount(finalLine) <= MaxSessionLogBytes)
                {
                    File.AppendAllText(_logPath, finalLine, encoding);
                }

                _sizeLimitReached = true;
                return;
            }

            File.AppendAllText(_logPath, line, encoding);
        }
        catch
        {
            _loggingFailed = true;
        }
    }

    private static string? CreateSessionLogPath()
    {
        var fileName = $"debug-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log";
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                var directory = Path.Combine(localAppData, "CodexQuotaHud", "logs");
                Directory.CreateDirectory(directory);
                return Path.Combine(directory, fileName);
            }
        }
        catch
        {
        }

        try
        {
            var fallbackDirectory = Path.Combine(Environment.CurrentDirectory, "logs");
            Directory.CreateDirectory(fallbackDirectory);
            return Path.Combine(fallbackDirectory, fileName);
        }
        catch
        {
            return null;
        }
    }

    private static void CleanupOldSessionLogs()
    {
        try
        {
            if (_logPath is null)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_logPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            var files = Directory
                .EnumerateFiles(directory, "debug-*.log", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Skip(MaxSessionLogs)
                .ToArray();

            foreach (var file in files)
            {
                try
                {
                    file.Delete();
                }
                catch (Exception ex)
                {
                    WriteLineLocked($"[LOG] warning failed to delete old session log file={file.FullName} error={ex.GetType().Name}: {ex.Message}", force: true);
                }
            }
        }
        catch
        {
        }
    }

    private static string GetVersion()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string SanitizeToken(string value)
    {
        value = SanitizeLogMessage(value);
        return value.Replace("[", string.Empty, StringComparison.Ordinal).Replace("]", string.Empty, StringComparison.Ordinal);
    }

    private static string SanitizeLogMessage(string message)
    {
        try
        {
            message = (message ?? string.Empty).ReplaceLineEndings(" ").Replace('\t', ' ').Trim();
            while (message.Contains("  ", StringComparison.Ordinal))
            {
                message = message.Replace("  ", " ", StringComparison.Ordinal);
            }

            return message.Length <= MaxLineLength ? message : message[..MaxLineLength] + "...";
        }
        catch
        {
            return "log message unavailable";
        }
    }
}
