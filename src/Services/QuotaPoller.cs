namespace CodexQuotaHud;

public sealed class QuotaPoller : IAsyncDisposable
{
    private readonly CodexQuotaProvider _codexProvider = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private ManagedAgyQuotaProvider? _agyProvider;
    private AppSettings _settings = AppSettings.Default();
    private bool _disposed;

    public async Task ApplySettingsAsync(AppSettings settings)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _refreshGate.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_disposed)
            {
                return;
            }

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
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task<IReadOnlyList<ProviderQuotaSnapshot>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || cancellationToken.IsCancellationRequested)
        {
            return Array.Empty<ProviderQuotaSnapshot>();
        }

        try
        {
            if (!await _refreshGate.WaitAsync(0, cancellationToken))
            {
                return Array.Empty<ProviderQuotaSnapshot>();
            }
        }
        catch (ObjectDisposedException)
        {
            return Array.Empty<ProviderQuotaSnapshot>();
        }

        try
        {
            if (_disposed || cancellationToken.IsCancellationRequested)
            {
                return Array.Empty<ProviderQuotaSnapshot>();
            }

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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _refreshGate.WaitAsync();
        try
        {
            await _codexProvider.DisposeAsync();
            if (_agyProvider is not null)
            {
                await _agyProvider.DisposeAsync();
                _agyProvider = null;
            }
        }
        finally
        {
            _refreshGate.Release();
            _refreshGate.Dispose();
        }
    }
}
