using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CodexQuotaHud;

public sealed class CodexQuotaReader
{
    private const int TimeoutMilliseconds = 12_000;
    private const string InitializeRequest = "{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"codex-quota-hud\",\"title\":\"Codex Quota HUD\",\"version\":\"0.1.0\"},\"capabilities\":null}}";
    private const string ReadQuotaRequest = "{\"id\":2,\"method\":\"account/rateLimits/read\"}";

    private readonly QuotaParser _parser = new();

    public async Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var codexPath = ResolveCodexPath();
        DebugLogger.Log($"[CODEX-DIAG] starting codex app-server path={codexPath}");

        using var timeoutCts = new CancellationTokenSource(TimeoutMilliseconds);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = codexPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.StartInfo.ArgumentList.Add("app-server");
        process.StartInfo.ArgumentList.Add("--listen");
        process.StartInfo.ArgumentList.Add("stdio://");
        ApplyGitNonInteractiveEnvironment(process.StartInfo);
        DebugLogger.Log("[CODEX-DIAG] codex git non-interactive env applied");

        var stderr = new StringBuilder();

        try
        {
            ProcessDiagnostics.LogSnapshot("codex", "before-start", reason: "before-codex-start");
            process.Start();
            DebugLogger.Log($"[CODEX-DIAG] codex app-server started pid={process.Id}");
            ProcessDiagnostics.LogSnapshot("codex", "after-start", process.Id, "immediately-after-codex-start");
            _ = Task.Run(() => ProcessDiagnostics.MonitorAsync(
                provider: "codex",
                phase: "monitor",
                targetPid: process.Id,
                duration: TimeSpan.FromSeconds(30),
                interval: TimeSpan.FromMilliseconds(500),
                cancellationToken: CancellationToken.None,
                reason: "after-codex-start"));
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[CODEX-DIAG] start error: {Shorten(ex.Message, 180)}");
            throw new InvalidOperationException(codexPath == "codex" ? "codex not found" : $"failed to start codex: {ex.Message}", ex);
        }

        var stderrTask = Task.Run(async () =>
        {
            try
            {
                while (!linkedCts.Token.IsCancellationRequested)
                {
                    var line = await process.StandardError.ReadLineAsync(linkedCts.Token);
                    if (line is null)
                    {
                        break;
                    }

                    stderr.AppendLine(line);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }, CancellationToken.None);

        try
        {
            await process.StandardInput.WriteLineAsync(InitializeRequest.AsMemory(), linkedCts.Token);
            await process.StandardInput.FlushAsync(linkedCts.Token);
            await process.StandardInput.WriteLineAsync(ReadQuotaRequest.AsMemory(), linkedCts.Token);
            await process.StandardInput.FlushAsync(linkedCts.Token);

            var sawInitialize = false;
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(linkedCts.Token);
                if (line is null)
                {
                    throw new InvalidOperationException("app-server exited before rate limit response");
                }

                if (!TryParseJsonLine(line, out var document))
                {
                    continue;
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (!TryReadId(root, out var id))
                    {
                        continue;
                    }

                    if (id == 1)
                    {
                        ThrowIfError(root);
                        sawInitialize = true;
                        continue;
                    }

                    if (id != 2)
                    {
                        continue;
                    }

                    if (!sawInitialize)
                    {
                        DebugLogger.Log("[CODEX-DIAG] id=2 received before id=1");
                    }

                    DebugLogger.Log("[CODEX-DIAG] codex rate limit response received");
                    ThrowIfError(root);

                    if (!root.TryGetProperty("result", out var result))
                    {
                        throw new InvalidOperationException("missing result");
                    }

                    return _parser.ParseRateLimits(result, line);
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            DebugLogger.Log("[CODEX-DIAG] error: app-server timeout");
            throw new TimeoutException("app-server timeout");
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[CODEX-DIAG] error: {Shorten(ex.Message, 180)}");
            throw;
        }
        finally
        {
            LogBeforeKill(process);
            TryKill(process);
            LogAfterKill(process);
            try
            {
                await stderrTask.WaitAsync(TimeSpan.FromMilliseconds(500));
            }
            catch
            {
            }

            var stderrText = stderr.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(stderrText))
            {
                DebugLogger.Log("[CODEX-DIAG] stderr: " + Shorten(stderrText, 2000));
            }
        }
    }

    private static void ApplyGitNonInteractiveEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "false";
        startInfo.Environment["GCM_GUI_PROMPT"] = "false";
        startInfo.Environment["GIT_ASKPASS"] = string.Empty;
        startInfo.Environment["SSH_ASKPASS"] = string.Empty;
    }

    private static string ResolveCodexPath()
    {
        var envPath = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var binDir = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
        var fixedPath = Path.Combine(binDir, "codex.exe");
        if (File.Exists(fixedPath))
        {
            return fixedPath;
        }

        if (Directory.Exists(binDir))
        {
            var versionedPath = Directory
                .EnumerateDirectories(binDir)
                .Select(directory => Path.Combine(directory, "codex.exe"))
                .Where(File.Exists)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();

            if (versionedPath is not null)
            {
                return versionedPath.FullName;
            }
        }

        return "codex";
    }

    private static bool TryParseJsonLine(string line, out JsonDocument document)
    {
        try
        {
            document = JsonDocument.Parse(line);
            return true;
        }
        catch (JsonException)
        {
            document = null!;
            return false;
        }
    }

    private static bool TryReadId(JsonElement root, out int id)
    {
        id = default;
        if (!root.TryGetProperty("id", out var idElement))
        {
            return false;
        }

        if (idElement.ValueKind == JsonValueKind.Number)
        {
            return idElement.TryGetInt32(out id);
        }

        return idElement.ValueKind == JsonValueKind.String && int.TryParse(idElement.GetString(), out id);
    }

    private static void ThrowIfError(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error) || error.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message))
        {
            throw new InvalidOperationException(Shorten(message.ToString(), 300));
        }

        throw new InvalidOperationException(Shorten(error.ToString(), 300));
    }

    private static void LogBeforeKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                ProcessDiagnostics.LogSnapshot("codex", "before-kill", process.Id, "before-codex-kill");
            }
        }
        catch
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                var pid = process.Id;
                process.Kill(entireProcessTree: true);
                DebugLogger.Log($"[CODEX-DIAG] codex app-server killed pid={pid}");
            }
        }
        catch
        {
        }
    }

    private static void LogAfterKill(Process process)
    {
        try
        {
            ProcessDiagnostics.LogSnapshot("codex", "after-kill", process.Id, "after-codex-kill");
        }
        catch
        {
        }
    }



    private static string Shorten(string text, int maxLength)
    {
        text = text.Trim().ReplaceLineEndings(" ");
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
