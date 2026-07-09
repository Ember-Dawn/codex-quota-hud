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
    private readonly HttpClient _httpClient;
    private IReadOnlyList<QuotaBucketSnapshot> _lastBuckets = Array.Empty<QuotaBucketSnapshot>();
    private DateTime? _changedAt;
    private AppSettings _settings;
    private int? _port;
    private DateTime _nextEndpointDiscoveryAt = DateTime.MinValue;
    private int _endpointFailureCount;
    private int _endpointNotReadyCount;
    private bool _disposed;

    public ManagedAgyQuotaProvider(AppSettings settings)
    {
        _settings = settings.Clone();
        _process = new ManagedAgyProcess(_settings);
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) => IsLocalAgyEndpoint(request?.RequestUri, certificate)
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public string ProviderId => "agy";
    public string DisplayName => "AGY";
    public bool IsEnabled => _settings.EnableAntigravity;
    public bool IsProcessRunning => _process.IsRunning;
    public bool HasCachedEndpoint => _port.HasValue;
    public bool NeedsColdStart => !_process.IsRunning && !_port.HasValue;

    public void ApplySettings(AppSettings settings)
    {
        var wasEnabled = _settings.EnableAntigravity;
        var pathChanged = !string.Equals(_settings.AgyExecutablePath, settings.AgyExecutablePath, StringComparison.OrdinalIgnoreCase);
        _settings = settings.Clone();
        _process.ApplySettings(_settings);

        if ((!wasEnabled && _settings.EnableAntigravity) || pathChanged || !_settings.EnableAntigravity)
        {
            ResetEndpointState();
        }
    }

    public async Task<ProviderQuotaSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed)
        {
            return ProviderQuotaSnapshot.Disabled(ProviderId, DisplayName, "Gemini");
        }

        if (!_settings.EnableAntigravity)
        {
            return ProviderQuotaSnapshot.Disabled(ProviderId, DisplayName, "Gemini");
        }

        if (_port is null && !_process.IsRunning && IsEndpointDiscoveryBackedOff())
        {
            DebugLogger.Log($"[AGY-DIAG] endpoint discovery skipped by backoff next={_nextEndpointDiscoveryAt:yyyy-MM-dd HH:mm:ss.fff}Z");
            DebugLogger.Log($"[PROCESS-DIAG] provider=agy phase=endpoint-discovery event=skipped-by-backoff next={_nextEndpointDiscoveryAt:yyyy-MM-dd HH:mm:ss.fff}Z");
            return FailureSnapshot(QuotaProviderStatus.Offline, EndpointNotReadyMessage);
        }

        var startResult = await _process.EnsureRunningAsync(cancellationToken).ConfigureAwait(false);
        if (!startResult.Success || startResult.ProcessId is null)
        {
            return FailureSnapshot(QuotaProviderStatus.Offline, startResult.ErrorMessage ?? RequestFailedMessage);
        }

        try
        {
            var snapshot = await ReadWithRetryAsync(startResult.ProcessId.Value, cancellationToken).ConfigureAwait(false);
            snapshot.IsManagedProcess = true;
            snapshot.ProcessId = _process.ProcessId;
            snapshot.Port = _port;
            UpdateChangedAt(snapshot.Buckets);
            snapshot.ChangedAt = _changedAt;
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
        await _process.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<ProviderQuotaSnapshot> ReadWithRetryAsync(int processId, CancellationToken cancellationToken)
    {
        if (_port.HasValue)
        {
            try
            {
                var snapshot = await ReadFromPortAsync(_port.Value, timeout: TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);
                ResetEndpointFailures();
                return snapshot;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[AGY-DIAG] read from cached port failed port={_port.Value} error={Shorten(ex.Message, 180)}");
                DebugLogger.Log($"[PROCESS-DIAG] provider=agy phase=endpoint-discovery event=cached-port-failed targetPid={processId} port={_port.Value} error={Shorten(ex.Message, 180)}");
                _port = null;
            }
        }

        if (IsEndpointDiscoveryBackedOff())
        {
            DebugLogger.Log($"[AGY-DIAG] endpoint discovery skipped by backoff next={_nextEndpointDiscoveryAt:yyyy-MM-dd HH:mm:ss.fff}Z");
            DebugLogger.Log($"[PROCESS-DIAG] provider=agy phase=endpoint-discovery event=skipped-by-backoff targetPid={processId} next={_nextEndpointDiscoveryAt:yyyy-MM-dd HH:mm:ss.fff}Z");
            throw new AgyEndpointNotReadyException();
        }

        DebugLogger.Log($"[AGY-DIAG] endpoint discovery start agyPid={processId}");
        DebugLogger.Log($"[PROCESS-DIAG] provider=agy phase=endpoint-discovery event=start targetPid={processId}");
        var discovered = await DiscoverReadyEndpointAsync(processId, TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        if (discovered is null)
        {
            await HandleEndpointDiscoveryFailureAsync(processId).ConfigureAwait(false);
            throw new AgyEndpointNotReadyException();
        }

        _port = discovered.Value.Port;
        ResetEndpointFailures();
        DebugLogger.Log($"[AGY-DIAG] endpoint ready agyPid={processId} port={_port}");
        DebugLogger.Log($"[PROCESS-DIAG] provider=agy phase=endpoint-discovery event=ready targetPid={processId} port={_port}");
        return discovered.Value.Snapshot;
    }

    private async Task<DiscoveredQuota?> DiscoverReadyEndpointAsync(int processId, TimeSpan maxWait, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + maxWait;
        var probedPorts = new HashSet<int>();

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ports = await AgyEndpointDiscovery.FindListenPortsAsync(processId, cancellationToken).ConfigureAwait(false);
            foreach (var port in ports)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!probedPorts.Add(port) && DateTime.UtcNow + TimeSpan.FromSeconds(1) < deadline)
                {
                    continue;
                }

                try
                {
                    var snapshot = await ReadFromPortAsync(port, timeout: TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                    return new DiscoveredQuota(port, snapshot);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<ProviderQuotaSnapshot> ReadFromPortAsync(int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://127.0.0.1:{port}{EndpointPath}");
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(RequestBody, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Connect-Protocol-Version", "1");

        try
        {
            using var response = await _httpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
            var snapshot = _parser.Parse(json);
            snapshot.Port = port;
            return snapshot;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException("AGY quota request timeout");
        }
    }

    private bool IsEndpointDiscoveryBackedOff()
    {
        return DateTime.UtcNow < _nextEndpointDiscoveryAt;
    }

    private async Task HandleEndpointDiscoveryFailureAsync(int processId)
    {
        _endpointFailureCount++;
        _endpointNotReadyCount++;
        var delay = _endpointFailureCount switch
        {
            1 => TimeSpan.FromSeconds(15),
            2 => TimeSpan.FromSeconds(30),
            _ => TimeSpan.FromSeconds(60)
        };
        _nextEndpointDiscoveryAt = DateTime.UtcNow + delay;
        DebugLogger.Log($"[AGY-DIAG] endpoint discovery failed agyPid={processId} failureCount={_endpointFailureCount}");
        DebugLogger.Log($"[PROCESS-DIAG] provider=agy phase=endpoint-discovery event=failed targetPid={processId} failureCount={_endpointFailureCount} next={_nextEndpointDiscoveryAt:yyyy-MM-dd HH:mm:ss.fff}Z");

        if (_endpointNotReadyCount >= 3)
        {
            _port = null;
            DebugLogger.Log("[AGY-DIAG] endpoint not ready threshold reached; stopping managed process");
            await _process.ShutdownIfOwnedAsync().ConfigureAwait(false);
        }
    }

    private void ResetEndpointFailures()
    {
        _endpointFailureCount = 0;
        _endpointNotReadyCount = 0;
        _nextEndpointDiscoveryAt = DateTime.MinValue;
    }

    private void ResetEndpointState()
    {
        _port = null;
        ResetEndpointFailures();
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

    private static string Shorten(string text, int maxLength)
    {
        text = text.Trim().ReplaceLineEndings(" ");
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }


    private readonly record struct DiscoveredQuota(int Port, ProviderQuotaSnapshot Snapshot);

    private sealed class AgyEndpointNotReadyException : Exception
    {
    }
}
