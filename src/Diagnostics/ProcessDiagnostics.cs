using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;

namespace CodexQuotaHud;

internal static class ProcessDiagnostics
{
    private const int MaxCommandLineLength = 500;

    private static readonly HashSet<string> InterestingProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "git",
        "cmd",
        "powershell",
        "pwsh",
        "conhost",
        "WindowsTerminal",
        "wt",
        "node",
        "agy",
        "codex",
        "netstat"
    };

    public static void LogInterestingProcesses(string reason)
    {
        try
        {
            var processes = ReadInterestingProcesses();
            DebugLogger.Log($"[AGY-DIAG] process snapshot reason={SanitizeField(reason)} count={processes.Count}");
            foreach (var process in processes.OrderBy(process => process.Name).ThenBy(process => process.ProcessId))
            {
                DebugLogger.Log("[AGY-DIAG] process snapshot " + FormatProcess(reason, process));
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogException("[AGY-DIAG] process snapshot failed", ex);
        }
    }

    public static async Task MonitorInterestingProcessesAsync(string reason, TimeSpan duration, TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            var seen = new HashSet<int>();
            var deadline = DateTime.UtcNow + duration;

            foreach (var process in ReadInterestingProcesses())
            {
                seen.Add(process.ProcessId);
            }

            DebugLogger.Log($"[AGY-DIAG] process monitor start reason={SanitizeField(reason)} durationMs={(int)duration.TotalMilliseconds} intervalMs={(int)interval.TotalMilliseconds} initialCount={seen.Count}");

            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                IReadOnlyList<ProcessInfo> processes;
                try
                {
                    processes = ReadInterestingProcesses();
                }
                catch (Exception ex)
                {
                    DebugLogger.LogException("[AGY-DIAG] process monitor read failed", ex);
                    processes = Array.Empty<ProcessInfo>();
                }

                foreach (var process in processes.OrderBy(process => process.ProcessId))
                {
                    if (seen.Add(process.ProcessId))
                    {
                        DebugLogger.Log("[AGY-DIAG] new process " + FormatProcess(reason, process));
                    }
                }

                try
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            DebugLogger.Log($"[AGY-DIAG] process monitor stop reason={SanitizeField(reason)}");
        }
        catch (Exception ex)
        {
            DebugLogger.LogException("[AGY-DIAG] process monitor failed", ex);
        }
    }

    private static IReadOnlyList<ProcessInfo> ReadInterestingProcesses()
    {
        var byPid = ReadWmiProcessInfo();
        var results = new List<ProcessInfo>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                int processId;
                string processName;
                try
                {
                    processId = process.Id;
                    processName = process.ProcessName;
                }
                catch
                {
                    continue;
                }

                if (!InterestingProcessNames.Contains(processName))
                {
                    continue;
                }

                byPid.TryGetValue(processId, out var wmi);
                var path = wmi?.ExecutablePath ?? SafeReadPath(process);
                var startTime = SafeReadStartTime(process);
                var parentName = wmi?.ParentProcessId is int parentPid ? ReadParentName(parentPid, byPid) : null;

                results.Add(new ProcessInfo(
                    processId,
                    processName,
                    wmi?.ParentProcessId,
                    parentName,
                    path,
                    SanitizeCommandLine(wmi?.CommandLine),
                    startTime));
            }
        }

        return results;
    }

    private static Dictionary<int, WmiProcessInfo> ReadWmiProcessInfo()
    {
        var processes = new Dictionary<int, WmiProcessInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process");
            using var collection = searcher.Get();
            foreach (ManagementObject item in collection)
            {
                try
                {
                    var processId = Convert.ToInt32(item["ProcessId"]);
                    var parentProcessId = item["ParentProcessId"] is null ? (int?)null : Convert.ToInt32(item["ParentProcessId"]);
                    var name = Convert.ToString(item["Name"]) ?? string.Empty;
                    var executablePath = Convert.ToString(item["ExecutablePath"]);
                    var commandLine = Convert.ToString(item["CommandLine"]);
                    processes[processId] = new WmiProcessInfo(processId, parentProcessId, name, executablePath, commandLine);
                }
                catch
                {
                }
                finally
                {
                    item.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogException("[AGY-DIAG] WMI process query failed", ex);
        }

        return processes;
    }

    private static string? ReadParentName(int parentProcessId, Dictionary<int, WmiProcessInfo> byPid)
    {
        if (byPid.TryGetValue(parentProcessId, out var parent) && !string.IsNullOrWhiteSpace(parent.Name))
        {
            return Path.GetFileNameWithoutExtension(parent.Name);
        }

        try
        {
            using var parentProcess = Process.GetProcessById(parentProcessId);
            return parentProcess.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeReadPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
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

    private static string FormatProcess(string reason, ProcessInfo process)
    {
        return $"reason={SanitizeField(reason)} name={SanitizeField(process.Name)} pid={process.ProcessId} ppid={FormatNullable(process.ParentProcessId)} parent={SanitizeField(process.ParentName)} path={SanitizeField(process.ExecutablePath)} start={FormatDateTime(process.StartTime)} cmd=\"{SanitizeField(process.CommandLine)}\"";
    }

    private static string SanitizeCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return "?";
        }

        try
        {
            var sanitized = commandLine.ReplaceLineEndings(" ").Replace('\t', ' ').Trim();
            sanitized = Regex.Replace(sanitized, "\\s+", " ");
            sanitized = Regex.Replace(sanitized, "(?i)(csrf_token|csrf|authorization|access_token|refresh_token|id_token|token|api[_-]?key|password|secret)(\\s*[=:]\\s*)(\\\"[^\\\"]*\\\"|'[^']*'|\\S+)", "$1$2***");
            sanitized = Regex.Replace(sanitized, "(?i)(bearer)\\s+\\S+", "$1 ***");
            return sanitized.Length <= MaxCommandLineLength ? sanitized : sanitized[..MaxCommandLineLength] + "...";
        }
        catch
        {
            return "?";
        }
    }

    private static string SanitizeField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "?";
        }

        try
        {
            value = value.ReplaceLineEndings(" ").Replace('\t', ' ').Trim();
            value = Regex.Replace(value, "\\s+", " ");
            return value.Length <= MaxCommandLineLength ? value : value[..MaxCommandLineLength] + "...";
        }
        catch
        {
            return "?";
        }
    }

    private static string FormatNullable(int? value) => value?.ToString() ?? "?";

    private static string FormatDateTime(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "?";

    private sealed record WmiProcessInfo(int ProcessId, int? ParentProcessId, string Name, string? ExecutablePath, string? CommandLine);

    private sealed record ProcessInfo(int ProcessId, string Name, int? ParentProcessId, string? ParentName, string? ExecutablePath, string CommandLine, DateTime? StartTime);
}
