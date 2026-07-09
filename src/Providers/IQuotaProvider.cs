namespace CodexQuotaHud;

public interface IQuotaProvider : IAsyncDisposable
{
    string ProviderId { get; }
    string DisplayName { get; }
    bool IsEnabled { get; }

    Task<ProviderQuotaSnapshot> RefreshAsync(CancellationToken cancellationToken = default);
}
