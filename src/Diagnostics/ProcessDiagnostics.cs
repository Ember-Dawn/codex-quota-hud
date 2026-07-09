using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;

namespace CodexQuotaHud;

internal static class ProcessDiagnostics
{
    private const int MaxCommandLineLength = 500;

    private static readonly HashSet<string> InterestingProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "codex",
        "CodexQuotaHud",
        "CodexQuotaHud-win-x64-no-dotnet",
        "CodexQuotaHud-win-x64-with-dotnet",
        "agy",
        "conhost",
        "cmd",
        "powershell",
        "pwsh",
        "git",
        "git-remote-https",
        "node",
        "python",
        "netstat",
        "WindowsTerminal",
        "wt"
    };

    public static void LogSnapshot(string provider, string phase, int? targetPid = null, string? reason = null)
    {
        if (!DebugLogger.IsDiagnosticLoggingEnabled)
        {
            return;
        }

        try
        {
            var processes = ReadInterestingProcesses();
            DebugLogger.Log($"[PROCESS-DIAG] provider={SanitizeField(provider)} phase={SanitizeField(phase)} event=snapshot count={processes.Count} targetPid={FormatNullable(targetPid)} reason={SanitizeField(reason)}");
            foreach (var process in processes.OrderBy(process => process.Name).ThenBy(process => process.ProcessId))
            {
                DebugLogger.Log(FormatLogLine(provider, phase, "process", process, targetPid, reason));
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogException($"[PROCESS-DIAG] provider={SanitizeField(provider)} phase={SanitizeField(phase)} event=snapshot-failed", ex);
        }
    }

    public static async Task<ProcessMonitorSummary> MonitorAsync(string provider, string phase, int targetPid, TimeSpan duration, TimeSpan interval, CancellationToken cancellationToken, string? reason = null)
    {
        var summary = new ProcessMonitorSummary();
        if (!DebugLogger.IsDiagnosticLoggingEnabled)
        {
            return summary;
        }

        try
        {
            var seen = new HashSet<int>();
            var deadline = DateTime.UtcNow + duration;

            foreach (var process in ReadInterestingProcesses())
            {
                seen.Add(process.ProcessId);
            }

            DebugLogger.Log($"[PROCESS-DIAG] provider={SanitizeField(provider)} phase={SanitizeField(phase)} event=monitor-start targetPid={targetPid} durationMs={(int)duration.TotalMilliseconds} intervalMs={(int)interval.TotalMilliseconds} initialCount={seen.Count} reason={SanitizeField(reason)}");

            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested && DebugLogger.IsDiagnosticLoggingEnabled)
            {
                IReadOnlyList<ProcessInfoSnapshot> processes;
                try
                {
                    processes = ReadInterestingProcesses();
                }
                catch (Exception ex)
                {
                    DebugLogger.LogException($"[PROCESS-DIAG] provider={SanitizeField(provider)} phase={SanitizeField(phase)} event=monitor-read-failed", ex);
                    processes = Array.Empty<ProcessInfoSnapshot>();
                }

                if (cancellationToken.IsCancellationRequested || !DebugLogger.IsDiagnosticLoggingEnabled)
                {
                    break;
                }

                foreach (var process in processes.OrderBy(process => process.ProcessId))
                {
                    if (cancellationToken.IsCancellationRequested || !DebugLogger.IsDiagnosticLoggingEnabled)
                    {
                        break;
                    }

                    if (seen.Add(process.ProcessId))
                    {
                        var relation = DetermineRelation(provider, process, targetPid);
                        if (relation == "descendant")
                        {
                            if (IsGitProcess(process.Name))
                            {
                                summary.GitDescendantSeen = true;
                            }

                            if (process.Name.Equals("conhost", StringComparison.OrdinalIgnoreCase))
                            {
                                summary.ConhostDescendantSeen = true;
                            }
                        }

                        DebugLogger.Log(FormatLogLine(provider, phase, "new-process", process, targetPid, reason));
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

            var stopEvent = cancellationToken.IsCancellationRequested ? "monitor-cancelled" : "monitor-stop";
            DebugLogger.Log($"[PROCESS-DIAG] provider={SanitizeField(provider)} phase={SanitizeField(phase)} event={stopEvent} targetPid={targetPid} reason={SanitizeField(reason)} gitDescendantSeen={summary.GitDescendantSeen} conhostDescendantSeen={summary.ConhostDescendantSeen}");
            return summary;
        }
        catch (Exception ex)
        {
            DebugLogger.LogException($"[PROCESS-DIAG] provider={SanitizeField(provider)} phase={SanitizeField(phase)} event=monitor-failed", ex);
            return summary;
        }
    }

    public static string SanitizeForLog(string? value, int maxLength = MaxCommandLineLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "?";
        }

        try
        {
            var sanitized = value.ReplaceLineEndings(" ").Replace('\t', ' ').Trim();
            sanitized = Regex.Replace(sanitized, "\\s+", " ");
            sanitized = Regex.Replace(sanitized, "(?i)(csrf_token|csrf|authorization|access_token|refresh_token|id_token|token|api[_-]?key|password|passwd|credential|secret)(\\s*[=:]\\s*)(\\\"[^\\\"]*\\\"|'[^']*'|\\S+)", "$1$2[redacted]");
            sanitized = Regex.Replace(sanitized, "(?i)(Authorization:\\s*)\\S+", "$1[redacted]");
            sanitized = Regex.Replace(sanitized, "(?i)(bearer)\\s+\\S+", "$1 [redacted]");
            return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength] + "...";
        }
        catch
        {
            return "?";
        }
    }

    private static IReadOnlyList<ProcessInfoSnapshot> ReadInterestingProcesses()
    {
        var byPid = ReadWmiProcessInfo();
        var results = new List<ProcessInfoSnapshot>();

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

                byPid.TryGetValue(processId, out var wmi);
                var wmiName = Path.GetFileNameWithoutExtension(wmi?.Name ?? string.Empty);
                if (!IsInteresting(processName) && !IsInteresting(wmiName))
                {
                    continue;
                }

                var path = wmi?.ExecutablePath ?? SafeReadPath(process);
                var startTime = SafeReadStartTime(process);
                var parentName = wmi?.ParentProcessId is int parentPid ? ReadParentName(parentPid, byPid) : null;

                results.Add(new ProcessInfoSnapshot(
                    processId,
                    string.IsNullOrWhiteSpace(wmiName) ? processName : wmiName,
                    wmi?.ParentProcessId,
                    parentName,
                    path,
                    SanitizeForLog(wmi?.CommandLine),
                    startTime,
                    BuildParentChain(processId, byPid)));
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
            DebugLogger.LogException("[PROCESS-DIAG] provider=app phase=wmi event=query-failed", ex);
        }

        return processes;
    }

    private static IReadOnlyList<int> BuildParentChain(int processId, Dictionary<int, WmiProcessInfo> byPid)
    {
        var chain = new List<int>();
        var seen = new HashSet<int> { processId };
        var current = processId;

        for (var i = 0; i < 64; i++)
        {
            if (!byPid.TryGetValue(current, out var info) || info.ParentProcessId is not int parentPid || parentPid <= 0)
            {
                break;
            }

            chain.Add(parentPid);
            if (!seen.Add(parentPid))
            {
                break;
            }

            current = parentPid;
        }

        return chain;
    }

    private static string DetermineRelation(string provider, ProcessInfoSnapshot process, int? targetPid)
    {
        if (targetPid.HasValue)
        {
            if (process.ProcessId == targetPid.Value)
            {
                return "target";
            }

            if (process.ParentChain.Contains(targetPid.Value))
            {
                return "descendant";
            }
        }

        return IsProviderRelated(provider, process) ? "related" : "unrelated";
    }

    private static bool IsProviderRelated(string provider, ProcessInfoSnapshot process)
    {
        if (provider.Equals("codex", StringComparison.OrdinalIgnoreCase))
        {
            return process.Name.Contains("codex", StringComparison.OrdinalIgnoreCase) || process.CommandLine.Contains("codex", StringComparison.OrdinalIgnoreCase);
        }

        if (provider.Equals("agy", StringComparison.OrdinalIgnoreCase))
        {
            return process.Name.Equals("agy", StringComparison.OrdinalIgnoreCase) || process.CommandLine.Contains("agy", StringComparison.OrdinalIgnoreCase) || process.CommandLine.Contains("antigravity", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string FormatLogLine(string provider, string phase, string eventName, ProcessInfoSnapshot process, int? targetPid, string? reason)
    {
        var relation = DetermineRelation(provider, process, targetPid);
        return $"[PROCESS-DIAG] provider={SanitizeField(provider)} phase={SanitizeField(phase)} event={SanitizeField(eventName)} relation={relation} targetPid={FormatNullable(targetPid)} reason={SanitizeField(reason)} name={SanitizeField(process.Name)} pid={process.ProcessId} ppid={FormatNullable(process.ParentProcessId)} parent={SanitizeField(process.ParentName)} path={SanitizeField(process.ExecutablePath)} start={FormatDateTime(process.StartTime)} cmd=\"{SanitizeField(process.CommandLine)}\"";
    }

    private static bool IsInteresting(string processName)
    {
        return !string.IsNullOrWhiteSpace(processName) && InterestingProcessNames.Contains(processName);
    }

    private static bool IsGitProcess(string processName)
    {
        return processName.Equals("git", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("git-remote-https", StringComparison.OrdinalIgnoreCase);
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

    private static string SanitizeField(string? value) => SanitizeForLog(value);

    private static string FormatNullable(int? value) => value?.ToString() ?? "?";

    private static string FormatDateTime(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "?";

    private sealed record WmiProcessInfo(int ProcessId, int? ParentProcessId, string Name, string? ExecutablePath, string? CommandLine);
}
