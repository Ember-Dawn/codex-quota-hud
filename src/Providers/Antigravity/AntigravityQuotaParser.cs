using System.Globalization;
using System.Text.Json;

namespace CodexQuotaHud;

public sealed class AntigravityQuotaParser
{
    public ProviderQuotaSnapshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement);
    }

    public ProviderQuotaSnapshot Parse(JsonElement root)
    {
        if (!TryGetProperty(root, "response", out var response) ||
            !TryGetProperty(response, "groups", out var groups) ||
            groups.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("missing AGY quota response groups");
        }

        QuotaBucketSnapshot? weekly = null;
        QuotaBucketSnapshot? fiveHour = null;

        foreach (var group in groups.EnumerateArray())
        {
            if (!TryGetProperty(group, "buckets", out var buckets) || buckets.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var bucket in buckets.EnumerateArray())
            {
                if (!TryGetString(bucket, "bucketId", out var bucketId))
                {
                    continue;
                }

                if (string.Equals(bucketId, "gemini-weekly", StringComparison.OrdinalIgnoreCase))
                {
                    weekly = ParseBucket(bucket, bucketId, "7d");
                }
                else if (string.Equals(bucketId, "gemini-5h", StringComparison.OrdinalIgnoreCase))
                {
                    fiveHour = ParseBucket(bucket, bucketId, "5h");
                }
            }
        }

        if (weekly is null || fiveHour is null)
        {
            throw new InvalidOperationException("missing AGY Gemini quota buckets");
        }

        return new ProviderQuotaSnapshot
        {
            ProviderId = "agy",
            DisplayName = "AGY",
            Subtitle = "Gemini",
            Source = "Managed AGY",
            Status = QuotaProviderStatus.Ok,
            Buckets = new List<QuotaBucketSnapshot> { weekly, fiveHour },
            UpdatedAt = DateTime.Now,
            CheckedAt = DateTime.Now
        };
    }

    private static QuotaBucketSnapshot ParseBucket(JsonElement bucket, string id, string label)
    {
        var remainingPercent = TryReadRemainingFraction(bucket, out var fraction)
            ? Math.Clamp(Math.Round(fraction * 100d), 0, 100)
            : (double?)null;

        DateTime? rawResetUtc = null;
        DateTime? localReset = null;
        if (TryGetString(bucket, "resetTime", out var resetTime) &&
            DateTimeOffset.TryParse(resetTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var resetAt))
        {
            rawResetUtc = resetAt.UtcDateTime;
            localReset = resetAt.LocalDateTime;
        }

        return new QuotaBucketSnapshot
        {
            Id = id,
            Label = label,
            ShortLabel = label,
            RemainingPercent = remainingPercent,
            ResetAt = localReset,
            RawResetTimeUtc = rawResetUtc
        };
    }

    private static bool TryReadRemainingFraction(JsonElement bucket, out double value)
    {
        if (TryReadDouble(bucket, "remainingFraction", out value))
        {
            return true;
        }

        if (TryGetProperty(bucket, "remaining", out var remaining) && TryReadDouble(remaining, "remainingFraction", out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value);
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(element, name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }

    private static bool TryReadDouble(JsonElement element, string name, out double value)
    {
        value = default;
        if (!TryGetProperty(element, name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            return property.TryGetDouble(out value);
        }

        return property.ValueKind == JsonValueKind.String && double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
