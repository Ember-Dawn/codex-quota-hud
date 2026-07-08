using System.Text.Json;

namespace CodexQuotaHud;

public sealed class QuotaParser
{
    public QuotaSnapshot ParseRateLimits(JsonElement result, string rawJson)
    {
        var snapshot = new QuotaSnapshot { UpdatedAt = DateTime.Now };

        if (!TryGetProperty(result, "rateLimitsByLimitId", out var byLimitId) ||
            !TryGetProperty(byLimitId, "codex", out var codex))
        {
            throw new InvalidOperationException("missing rateLimitsByLimitId.codex");
        }

        var primary = TryGetProperty(codex, "primary", out var primaryValue) ? primaryValue : (JsonElement?)null;
        var secondary = TryGetProperty(codex, "secondary", out var secondaryValue) ? secondaryValue : (JsonElement?)null;
        var windows = new List<QuotaWindow>();

        if (primary.HasValue)
        {
            AddWindow(windows, primary.Value);
        }

        if (secondary.HasValue)
        {
            AddWindow(windows, secondary.Value);
        }

        foreach (var window in windows)
        {
            if (window.WindowDurationMins >= 9_000)
            {
                snapshot.SevenDay = window;
            }
            else if (window.WindowDurationMins is >= 200 and <= 600)
            {
                snapshot.FiveHour = window;
            }
        }

        if (snapshot.SevenDay.RemainingPercent is null && snapshot.FiveHour.RemainingPercent is null)
        {
            // Fallback for protocol variants without windowDurationMins.
            if (primary.HasValue)
            {
                snapshot.SevenDay = ParseWindow(primary.Value);
            }

            if (secondary.HasValue)
            {
                snapshot.FiveHour = ParseWindow(secondary.Value);
            }
        }

        if (snapshot.SevenDay.RemainingPercent is null && snapshot.FiveHour.RemainingPercent is null)
        {
            throw new InvalidOperationException("unknown rate limit response");
        }

        return snapshot;
    }

    public QuotaSnapshot FromError(string error)
    {
        return new QuotaSnapshot
        {
            UpdatedAt = DateTime.Now,
            ErrorMessage = Shorten(error)
        };
    }

    private static void AddWindow(List<QuotaWindow> windows, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            windows.Add(ParseWindow(element));
        }
    }

    private static QuotaWindow ParseWindow(JsonElement element)
    {
        return new QuotaWindow
        {
            RemainingPercent = TryReadDouble(element, "usedPercent", out var usedPercent)
                ? Math.Clamp(Math.Round(100 - usedPercent), 0, 100)
                : null,
            ResetAt = TryReadUnixSeconds(element, "resetsAt", out var resetAt) ? resetAt : null,
            WindowDurationMins = TryReadInt(element, "windowDurationMins", out var mins) ? mins : null
        };
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value);
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

        return property.ValueKind == JsonValueKind.String && double.TryParse(property.GetString(), out value);
    }

    private static bool TryReadInt(JsonElement element, string name, out int value)
    {
        value = default;
        if (!TryGetProperty(element, name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            return property.TryGetInt32(out value);
        }

        return property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value);
    }

    private static bool TryReadUnixSeconds(JsonElement element, string name, out DateTime value)
    {
        value = default;
        if (!TryGetProperty(element, name, out var property))
        {
            return false;
        }

        long seconds;
        if (property.ValueKind == JsonValueKind.Number)
        {
            if (!property.TryGetInt64(out seconds))
            {
                return false;
            }
        }
        else if (property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out var parsed))
        {
            seconds = parsed;
        }
        else
        {
            return false;
        }

        value = DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
        return true;
    }

    private static string Shorten(string text)
    {
        text = text.Trim().ReplaceLineEndings(" ");
        return text.Length <= 180 ? text : text[..180] + "...";
    }
}
