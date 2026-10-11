namespace LootSingles.Infrastructure.Tcgplayer;

/// <summary>
/// Holds the store key resolved through <c>GET /stores/self</c> for the life of the process
/// (research.md §2). A singleton, because <see cref="TcgplayerApiClient"/> is a transient typed
/// client. Concurrent first lookups share one call; a failed lookup is not cached.
/// </summary>
public sealed class TcgplayerStoreKeyCache
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile string? _storeKey;

    public async Task<string> GetAsync(
        Func<CancellationToken, Task<string>> resolve,
        CancellationToken cancellationToken
    )
    {
        if (_storeKey is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _storeKey ??= await resolve(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
