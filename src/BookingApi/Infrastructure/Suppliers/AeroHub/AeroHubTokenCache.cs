namespace BookingApi.Infrastructure.Suppliers.AeroHub;

public sealed class AeroHubTokenCache
{
    private static readonly TimeSpan ExpirySafetyMargin = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private CachedToken? _current;

    public AeroHubTokenCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    internal async Task<string> GetTokenAsync(Func<CancellationToken, Task<AeroHubTokenResponse>> fetchToken, CancellationToken cancellationToken)
    {
        if (ValidTokenOrNull() is { } cachedToken)
        {
            return cachedToken;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (ValidTokenOrNull() is { } refreshedByAnotherCaller)
            {
                return refreshedByAnotherCaller;
            }

            var response = await fetchToken(cancellationToken);
            var expiresAt = _timeProvider.GetUtcNow().AddSeconds(response.ExpiresIn) - ExpirySafetyMargin;
            _current = new CachedToken(response.AccessToken, expiresAt);
            return response.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Invalidate(string rejectedToken)
    {
        var current = _current;
        if (current?.Value == rejectedToken)
        {
            Interlocked.CompareExchange(ref _current, null, current);
        }
    }

    private string? ValidTokenOrNull()
    {
        var current = _current;
        return current is not null && _timeProvider.GetUtcNow() < current.ExpiresAt ? current.Value : null;
    }

    private sealed record CachedToken(string Value, DateTimeOffset ExpiresAt);
}
