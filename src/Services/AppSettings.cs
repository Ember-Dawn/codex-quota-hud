namespace CodexQuotaHud;

public sealed class AppSettings
{
    public const int DefaultAutoRefreshSeconds = 60;
    public const string DefaultSevenDayColor = "#4EA1FF";
    public const string DefaultFiveHourColor = "#C2410C";
    public const string DefaultTrackColor = "#303740";
    public const string DefaultTrackBorderColor = "#7A8796";
    public const string DefaultAntigravityMode = "managed-agy";

    public int AutoRefreshSeconds { get; set; } = DefaultAutoRefreshSeconds;
    public string SevenDayColor { get; set; } = DefaultSevenDayColor;
    public string FiveHourColor { get; set; } = DefaultFiveHourColor;
    public string TrackColor { get; set; } = DefaultTrackColor;
    public string TrackBorderColor { get; set; } = DefaultTrackBorderColor;
    public bool EnableAntigravity { get; set; }
    public string AntigravityMode { get; set; } = DefaultAntigravityMode;
    public bool StartAgyHidden { get; set; } = true;
    public bool CloseManagedAgyOnExit { get; set; } = true;
    public string? AgyExecutablePath { get; set; }

    public static AppSettings Default() => new();

    public AppSettings Clone()
    {
        return new AppSettings
        {
            AutoRefreshSeconds = AutoRefreshSeconds,
            SevenDayColor = SevenDayColor,
            FiveHourColor = FiveHourColor,
            TrackColor = TrackColor,
            TrackBorderColor = TrackBorderColor,
            EnableAntigravity = EnableAntigravity,
            AntigravityMode = AntigravityMode,
            StartAgyHidden = StartAgyHidden,
            CloseManagedAgyOnExit = CloseManagedAgyOnExit,
            AgyExecutablePath = AgyExecutablePath
        };
    }
}
