using System.Text.Json;

namespace CodexQuotaHud;

public static class SettingsStore
{
    public static string SettingsPath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "CodexQuotaHud", "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return AppSettings.Default();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? AppSettings.Default();
            return Normalize(settings);
        }
        catch
        {
            return AppSettings.Default();
        }
    }

    public static void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(Normalize(settings), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var allowedSeconds = new HashSet<int> { 30, 60, 300, 600, 1200 };
        if (!allowedSeconds.Contains(settings.AutoRefreshSeconds))
        {
            settings.AutoRefreshSeconds = AppSettings.DefaultAutoRefreshSeconds;
        }

        if (!SettingsForm.IsValidHexColor(settings.SevenDayColor))
        {
            settings.SevenDayColor = AppSettings.DefaultSevenDayColor;
        }

        if (!SettingsForm.IsValidHexColor(settings.FiveHourColor))
        {
            settings.FiveHourColor = AppSettings.DefaultFiveHourColor;
        }

        if (!SettingsForm.IsValidHexColor(settings.TrackColor))
        {
            settings.TrackColor = AppSettings.DefaultTrackColor;
        }

        if (!SettingsForm.IsValidHexColor(settings.TrackBorderColor))
        {
            settings.TrackBorderColor = AppSettings.DefaultTrackBorderColor;
        }

        if (!string.Equals(settings.AntigravityMode, AppSettings.DefaultAntigravityMode, StringComparison.OrdinalIgnoreCase))
        {
            settings.AntigravityMode = AppSettings.DefaultAntigravityMode;
        }

        if (!string.IsNullOrWhiteSpace(settings.AgyExecutablePath) && !File.Exists(settings.AgyExecutablePath))
        {
            settings.AgyExecutablePath = null;
        }

        settings.SevenDayColor = settings.SevenDayColor.ToUpperInvariant();
        settings.FiveHourColor = settings.FiveHourColor.ToUpperInvariant();
        settings.TrackColor = settings.TrackColor.ToUpperInvariant();
        settings.TrackBorderColor = settings.TrackBorderColor.ToUpperInvariant();
        settings.AntigravityMode = settings.AntigravityMode.ToLowerInvariant();
        return settings;
    }
}
