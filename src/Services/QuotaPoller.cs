namespace CodexQuotaHud;

public sealed class QuotaPoller : IAsyncDisposable
{
    private readonly CodexQuotaProvider _codexProvider = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private ManagedAgyQuotaProvider? _agyProvider;
    private AppSettings _settings = AppSettings.Default();

    public async Task ApplySettingsAsync(AppSettings settings)
    {
        var previousAgyEnabled = _settings.EnableAntigravity;
        _settings = settings.Clone();

        if (_settings.EnableAntigravity)
        {
            _agyProvider ??= new ManagedAgyQuotaProvider(_settings);
            _agyProvider.ApplySettings(_settings);
            return;
        }

        if (previousAgyEnabled && _agyProvider is not null)
        {
            await _agyProvider.DisposeAsync();
            _agyProvider = null;
        }
    }

    public async Task<IReadOnlyList<ProviderQuotaSnapshot>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken))
        {
            return Array.Empty<ProviderQuotaSnapshot>();
        }

        try
        {
            var tasks = new List<Task<ProviderQuotaSnapshot>>
            {
                _codexProvider.RefreshAsync(cancellationToken)
            };

            if (_settings.EnableAntigravity)
            {
                _agyProvider ??= new ManagedAgyQuotaProvider(_settings);
                _agyProvider.ApplySettings(_settings);
                tasks.Add(_agyProvider.RefreshAsync(cancellationToken));
            }

            return await Task.WhenAll(tasks);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _codexProvider.DisposeAsync();
        if (_agyProvider is not null)
        {
            await _agyProvider.DisposeAsync();
            _agyProvider = null;
        }

        _refreshGate.Dispose();
    }
}
