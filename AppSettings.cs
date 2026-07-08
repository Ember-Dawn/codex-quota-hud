namespace CodexQuotaHud;

public sealed class AppSettings
{
    public const int DefaultAutoRefreshSeconds = 60;
    public const string DefaultSevenDayColor = "#4EA1FF";
    public const string DefaultFiveHourColor = "#FFB454";
    public const string DefaultTrackBorderColor = "#56606C";

    public int AutoRefreshSeconds { get; set; } = DefaultAutoRefreshSeconds;
    public string SevenDayColor { get; set; } = DefaultSevenDayColor;
    public string FiveHourColor { get; set; } = DefaultFiveHourColor;
    public string TrackBorderColor { get; set; } = DefaultTrackBorderColor;

    public static AppSettings Default() => new();

    public AppSettings Clone()
    {
        return new AppSettings
        {
            AutoRefreshSeconds = AutoRefreshSeconds,
            SevenDayColor = SevenDayColor,
            FiveHourColor = FiveHourColor,
            TrackBorderColor = TrackBorderColor
        };
    }
}
