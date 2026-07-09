using System.Diagnostics;
using System.Text;

namespace CodexQuotaHud;

public static class AgyEndpointDiscovery
{
    public static async Task<IReadOnlyList<int>> FindListenPortsAsync(int processId, CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "netstat",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.StartInfo.ArgumentList.Add("-ano");
        process.StartInfo.ArgumentList.Add("-p");
        process.StartInfo.ArgumentList.Add("tcp");

        try
        {
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            var output = await stdoutTask;
            _ = await stderrTask;
            return ParseNetstatPorts(output, processId);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (TimeoutException)
        {
            TryKill(process);
            return Array.Empty<int>();
        }
        catch
        {
            TryKill(process);
            return Array.Empty<int>();
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static IReadOnlyList<int> ParseNetstatPorts(string output, int processId)
    {
        var ports = new HashSet<int>();
        foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase) ||
                !line.Contains("LISTEN", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || !int.TryParse(parts[^1], out var pid) || pid != processId)
            {
                continue;
            }

            if (TryReadPort(parts[1], out var port))
            {
                ports.Add(port);
            }
        }

        return ports.OrderBy(port => port).ToArray();
    }

    private static bool TryReadPort(string localAddress, out int port)
    {
        port = default;
        var index = localAddress.LastIndexOf(':');
        if (index < 0 || index == localAddress.Length - 1)
        {
            return false;
        }

        return int.TryParse(localAddress[(index + 1)..], out port) && port > 0;
    }
}
