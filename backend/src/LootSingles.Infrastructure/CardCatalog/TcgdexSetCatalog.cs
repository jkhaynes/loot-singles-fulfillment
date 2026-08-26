using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LootSingles.Infrastructure.CardCatalog;

/// <summary>
/// App-lifetime cache of TCGdex's set name -> set id list (research.md §12). TCGdex's own API
/// guidance asks consumers to cache bulk, slow-changing reference data locally rather than
/// fetching it repeatedly; this list (218 sets, ~35KB) changes only a handful of times a year, so
/// a per-request cache (as <see cref="TcgdexCardCatalogProvider"/> alone previously had) still
/// refetched it on every single order-detail request. Registered as a singleton so the cache is
/// shared across every request, not just within one.
/// </summary>
public sealed class TcgdexSetCatalog(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider
)
{
    private const string HttpClientName = "tcgdex";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    private readonly Lock _lock = new();
    private Task<IReadOnlyDictionary<string, string>>? _setIdsByNameTask;
    private DateTimeOffset _fetchedAt;

    public async Task<IReadOnlyDictionary<string, string>> GetSetIdsByNameAsync(
        CancellationToken cancellationToken
    )
    {
        Task<IReadOnlyDictionary<string, string>> task;
        lock (_lock)
        {
            var now = timeProvider.GetUtcNow();
            if (_setIdsByNameTask is null || now - _fetchedAt >= CacheDuration)
            {
                _fetchedAt = now;
                _setIdsByNameTask = FetchSetIdsByNameAsync(cancellationToken);
            }

            task = _setIdsByNameTask;
        }

        try
        {
            return await task;
        }
        catch
        {
            // Don't let a transient failure poison the cache for the rest of CacheDuration - the
            // next call should attempt a fresh fetch rather than immediately rethrowing this same
            // cached failure for up to 24 hours.
            lock (_lock)
            {
                if (ReferenceEquals(_setIdsByNameTask, task))
                {
                    _setIdsByNameTask = null;
                }
            }

            throw;
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> FetchSetIdsByNameAsync(
        CancellationToken cancellationToken
    )
    {
        // A fresh client per fetch (rather than one held for the singleton's lifetime) so
        // IHttpClientFactory's normal handler rotation/DNS-refresh behavior is unaffected - this
        // runs at most a few times a day given CacheDuration, so there's no pooling benefit lost.
        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        var sets =
            await httpClient.GetFromJsonAsync<IReadOnlyList<SetSummary>>("sets", cancellationToken)
            ?? [];

        return sets.Where(set => set is { Id: not null, Name: not null })
            .ToDictionary(set => set.Name!, set => set.Id!, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record SetSummary(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name
    );
}
