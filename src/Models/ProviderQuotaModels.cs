namespace CodexQuotaHud;

public enum QuotaProviderStatus
{
    Disabled,
    Refreshing,
    Ok,
    Offline,
    Failed
}

public sealed class ProviderQuotaSnapshot
{
    public string ProviderId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string Source { get; set; } = string.Empty;
    public QuotaProviderStatus Status { get; set; } = QuotaProviderStatus.Refreshing;
    public List<QuotaBucketSnapshot> Buckets { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public DateTime? CheckedAt { get; set; }
    public DateTime? ChangedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsManagedProcess { get; set; }
    public int? ProcessId { get; set; }
    public int? Port { get; set; }

    public QuotaBucketSnapshot? FindBucket(string shortLabel)
    {
        return Buckets.FirstOrDefault(bucket =>
            string.Equals(bucket.ShortLabel, shortLabel, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(bucket.Label, shortLabel, StringComparison.OrdinalIgnoreCase));
    }

    public static ProviderQuotaSnapshot Disabled(string providerId, string displayName, string? subtitle = null)
    {
        return new ProviderQuotaSnapshot
        {
            ProviderId = providerId,
            DisplayName = displayName,
            Subtitle = subtitle,
            Status = QuotaProviderStatus.Disabled,
            UpdatedAt = DateTime.Now
        };
    }
}

public sealed class QuotaBucketSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ShortLabel { get; set; } = string.Empty;
    public double? RemainingPercent { get; set; }
    public DateTime? ResetAt { get; set; }
    public DateTime? RawResetTimeUtc { get; set; }

    public string PercentText => RemainingPercent.HasValue
        ? $"{RemainingPercent.Value:0}%"
        : "--";
}
