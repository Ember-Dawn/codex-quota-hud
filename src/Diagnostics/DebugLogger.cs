using System.Text;

namespace CodexQuotaHud;

internal static class DebugLogger
{
    private const int MaxLineLength = 1000;

    public static void Log(string message)
    {
        var sanitizedMessage = SanitizeLogMessage(message);
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {sanitizedMessage}{Environment.NewLine}";
            File.AppendAllText(GetLogPath(), line, Encoding.UTF8);
        }
        catch
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {sanitizedMessage}{Environment.NewLine}";
                File.AppendAllText(Path.Combine(Environment.CurrentDirectory, "debug.log"), line, Encoding.UTF8);
            }
            catch
            {
            }
        }
    }

    public static void LogException(string prefix, Exception ex)
    {
        try
        {
            Log($"{prefix}: {ex.GetType().Name}: {ex.Message}");
        }
        catch
        {
        }
    }

    private static string GetLogPath()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                var directory = Path.Combine(localAppData, "CodexQuotaHud");
                Directory.CreateDirectory(directory);
                return Path.Combine(directory, "debug.log");
            }
        }
        catch
        {
        }

        return Path.Combine(Environment.CurrentDirectory, "debug.log");
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
