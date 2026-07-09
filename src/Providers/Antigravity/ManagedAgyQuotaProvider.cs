using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CodexQuotaHud;

public sealed class ManagedAgyQuotaProvider : IQuotaProvider
{
    private const string EndpointPath = "/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary";
    private const string EndpointNotReadyMessage = "AGY started, but quota endpoint is not ready. Please run agy once manually to finish login/trust.";
    private const string RequestFailedMessage = "Failed to read AGY quota. Please make sure Antigravity CLI is installed and signed in.";
    private const string RequestBody = "{\"metadata\":{\"ideName\":\"antigravity\",\"extensionName\":\"antigravity\",\"locale\":\"en\",\"ideVersion\":\"unknown\"}}";

    private readonly AntigravityQuotaParser _parser = new();
    private readonly ManagedAgyProcess _process;
    private IReadOnlyList<QuotaBucketSnapshot> _lastBuckets = Array.Empty<QuotaBucketSnapshot>();
    private DateTime? _changedAt;
    private AppSettings _settings;
    private int? _port;

    public ManagedAgyQuotaProvider(AppSettings settings)
    {
        _settings = settings.Clone();
        _process = new ManagedAgyProcess(_settings);
    }

    public string ProviderId => "agy";
    public string DisplayName => "AGY";
    public bool IsEnabled => _settings.EnableAntigravity;

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings.Clone();
        _process.ApplySettings(_settings);
    }

    public async Task<ProviderQuotaSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.EnableAntigravity)
        {
            return ProviderQuotaSnapshot.Disabled(ProviderId, DisplayName, "Gemini");
        }

        var startResult = await _process.EnsureRunningAsync(cancellationToken);
        if (!startResult.Success || startResult.ProcessId is null)
        {
            return FailureSnapshot(QuotaProviderStatus.Offline, startResult.ErrorMessage ?? RequestFailedMessage);
        }

        try
        {
            var snapshot = await ReadWithRetryAsync(startResult.ProcessId.Value, cancellationToken);
            snapshot.IsManagedProcess = true;
            snapshot.ProcessId = _process.ProcessId;
            snapshot.Port = _port;
            UpdateChangedAt(snapshot.Buckets);
            snapshot.ChangedAt = _changedAt;
            return snapshot;
        }
        catch (AgyEndpointNotReadyException)
        {
            return FailureSnapshot(QuotaProviderStatus.Offline, EndpointNotReadyMessage);
        }
        catch
        {
            return FailureSnapshot(QuotaProviderStatus.Failed, RequestFailedMessage);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _process.DisposeAsync();
    }

    private async Task<ProviderQuotaSnapshot> ReadWithRetryAsync(int processId, CancellationToken cancellationToken)
    {
        if (_port.HasValue)
        {
            try
            {
                return await ReadFromPortAsync(_port.Value, timeout: TimeSpan.FromSeconds(8), cancellationToken);
            }
            catch
            {
                _port = null;
            }
        }

        var discovered = await DiscoverReadyEndpointAsync(processId, TimeSpan.FromSeconds(15), cancellationToken);
        if (discovered is null)
        {
            throw new AgyEndpointNotReadyException();
        }

        _port = discovered.Value.Port;
        return discovered.Value.Snapshot;
    }

    private async Task<DiscoveredQuota?> DiscoverReadyEndpointAsync(int processId, TimeSpan maxWait, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + maxWait;
        var probedPorts = new HashSet<int>();

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            var ports = await AgyEndpointDiscovery.FindListenPortsAsync(processId, cancellationToken);
            foreach (var port in ports)
            {
                if (!probedPorts.Add(port) && DateTime.UtcNow + TimeSpan.FromSeconds(1) < deadline)
                {
                    continue;
                }

                try
                {
                    var snapshot = await ReadFromPortAsync(port, timeout: TimeSpan.FromSeconds(2), cancellationToken);
                    return new DiscoveredQuota(port, snapshot);
                }
                catch
                {
                }
            }

            await Task.Delay(500, cancellationToken);
        }

        return null;
    }

    private async Task<ProviderQuotaSnapshot> ReadFromPortAsync(int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://127.0.0.1:{port}{EndpointPath}");
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) => IsLocalAgyEndpoint(request?.RequestUri, certificate)
        };
        using var client = new HttpClient(handler) { Timeout = timeout };
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(RequestBody, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Connect-Protocol-Version", "1");

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var snapshot = _parser.Parse(json);
        snapshot.Port = port;
        return snapshot;
    }

    private static bool IsLocalAgyEndpoint(Uri? requestUri, X509Certificate2? certificate)
    {
        return requestUri is not null &&
               string.Equals(requestUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(requestUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    private ProviderQuotaSnapshot FailureSnapshot(QuotaProviderStatus status, string errorMessage)
    {
        return new ProviderQuotaSnapshot
        {
            ProviderId = ProviderId,
            DisplayName = DisplayName,
            Subtitle = "Gemini",
            Source = "Managed AGY",
            Status = status,
            Buckets = CreateEmptyBuckets(),
            UpdatedAt = DateTime.Now,
            ErrorMessage = errorMessage,
            IsManagedProcess = _process.WasStartedByHud,
            ProcessId = _process.ProcessId,
            Port = _port
        };
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

    private static List<QuotaBucketSnapshot> CreateEmptyBuckets()
    {
        return new List<QuotaBucketSnapshot>
        {
            new() { Id = "gemini-weekly", Label = "7d", ShortLabel = "7d" },
            new() { Id = "gemini-5h", Label = "5h", ShortLabel = "5h" }
        };
    }

    private readonly record struct DiscoveredQuota(int Port, ProviderQuotaSnapshot Snapshot);

    private sealed class AgyEndpointNotReadyException : Exception
    {
    }
}
