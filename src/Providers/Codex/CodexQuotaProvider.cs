namespace CodexQuotaHud;

public sealed class CodexQuotaProvider : IQuotaProvider
{
    private readonly CodexQuotaReader _reader = new();
    private IReadOnlyList<QuotaBucketSnapshot> _lastBuckets = Array.Empty<QuotaBucketSnapshot>();
    private DateTime? _changedAt;

    public string ProviderId => "codex";
    public string DisplayName => "Codex";
    public bool IsEnabled => true;

    public async Task<ProviderQuotaSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var quota = await _reader.ReadAsync(cancellationToken);
            var buckets = new List<QuotaBucketSnapshot>
            {
                FromWindow("codex-7d", "7d", quota.SevenDay),
                FromWindow("codex-5h", "5h", quota.FiveHour)
            };

            UpdateChangedAt(buckets);

            return new ProviderQuotaSnapshot
            {
                ProviderId = ProviderId,
                DisplayName = DisplayName,
                Source = "Codex CLI",
                Status = QuotaProviderStatus.Ok,
                Buckets = buckets,
                UpdatedAt = quota.UpdatedAt,
                CheckedAt = DateTime.Now,
                ChangedAt = _changedAt,
                ErrorMessage = quota.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            return new ProviderQuotaSnapshot
            {
                ProviderId = ProviderId,
                DisplayName = DisplayName,
                Source = "Codex CLI",
                Status = QuotaProviderStatus.Failed,
                Buckets = CreateEmptyBuckets(),
                UpdatedAt = DateTime.Now,
                ErrorMessage = Shorten(ex.Message)
            };
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private void UpdateChangedAt(IReadOnlyList<QuotaBucketSnapshot> buckets)
    {
        if (_lastBuckets.Count == 0 || BucketsChanged(_lastBuckets, buckets))
        {
            _changedAt = DateTime.Now;
        }

        _lastBuckets = buckets.Select(CloneBucket).ToArray();
    }

    private static bool BucketsChanged(IReadOnlyList<QuotaBucketSnapshot> previous, IReadOnlyList<QuotaBucketSnapshot> current)
    {
        foreach (var bucket in current)
        {
            var old = previous.FirstOrDefault(item => item.Id == bucket.Id);
            if (old is null || Math.Round(old.RemainingPercent ?? -1, 2) != Math.Round(bucket.RemainingPercent ?? -1, 2))
            {
                return true;
            }
        }

        return false;
    }

    private static QuotaBucketSnapshot CloneBucket(QuotaBucketSnapshot bucket)
    {
        return new QuotaBucketSnapshot
        {
            Id = bucket.Id,
            Label = bucket.Label,
            ShortLabel = bucket.ShortLabel,
            RemainingPercent = bucket.RemainingPercent,
            ResetAt = bucket.ResetAt,
            RawResetTimeUtc = bucket.RawResetTimeUtc
        };
    }

    private static QuotaBucketSnapshot FromWindow(string id, string label, QuotaWindow window)
    {
        return new QuotaBucketSnapshot
        {
            Id = id,
            Label = label,
            ShortLabel = label,
            RemainingPercent = window.RemainingPercent,
            ResetAt = window.ResetAt
        };
    }

    private static List<QuotaBucketSnapshot> CreateEmptyBuckets()
    {
        return new List<QuotaBucketSnapshot>
        {
            new() { Id = "codex-7d", Label = "7d", ShortLabel = "7d" },
            new() { Id = "codex-5h", Label = "5h", ShortLabel = "5h" }
        };
    }

    private static string Shorten(string text)
    {
        text = text.Trim().ReplaceLineEndings(" ");
        return text.Length <= 180 ? text : text[..180] + "...";
    }
}
