namespace CodexQuotaHud;

public sealed class QuotaSnapshot
{
    public QuotaWindow SevenDay { get; set; } = new();
    public QuotaWindow FiveHour { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string? ErrorMessage { get; set; }
}

public sealed class QuotaWindow
{
    public double? RemainingPercent { get; set; }
    public DateTime? ResetAt { get; set; }
    public int? WindowDurationMins { get; set; }

    public string PercentText => RemainingPercent.HasValue
        ? $"{RemainingPercent.Value:0}%"
        : "--";

    public string ResetText => ResetAt.HasValue
        ? ResetAt.Value.ToString("MM-dd HH:mm")
        : "--";
}
