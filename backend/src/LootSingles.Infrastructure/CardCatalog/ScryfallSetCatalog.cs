using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LootSingles.Infrastructure.CardCatalog;

/// <summary>
/// App-lifetime cache of Scryfall's set name -> set code list (research.md §3), mirroring
/// <see cref="TcgdexSetCatalog"/>'s proven design: Scryfall's own guidance asks consumers to
/// cache bulk, slow-changing reference data locally rather than fetching it repeatedly, and this
/// list changes only when a new Magic set is released. Registered as a singleton so the cache is
/// shared across every request, not just within one.
/// </summary>
public sealed class ScryfallSetCatalog(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider
)
{
    private const string HttpClientName = "scryfall";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    private readonly Lock _lock = new();
    private Task<IReadOnlyDictionary<string, string>>? _setCodesByNameTask;
    private DateTimeOffset _fetchedAt;

    public async Task<IReadOnlyDictionary<string, string>> GetSetCodesByNameAsync(
        CancellationToken cancellationToken
    )
    {
        Task<IReadOnlyDictionary<string, string>> task;
        lock (_lock)
        {
            var now = timeProvider.GetUtcNow();
            if (_setCodesByNameTask is null || now - _fetchedAt >= CacheDuration)
            {
                _fetchedAt = now;
                _setCodesByNameTask = FetchSetCodesByNameAsync(cancellationToken);
            }

            task = _setCodesByNameTask;
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
                if (ReferenceEquals(_setCodesByNameTask, task))
                {
                    _setCodesByNameTask = null;
                }
            }

            throw;
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> FetchSetCodesByNameAsync(
        CancellationToken cancellationToken
    )
    {
        // A fresh client per fetch (rather than one held for the singleton's lifetime) so
        // IHttpClientFactory's normal handler rotation/DNS-refresh behavior is unaffected - this
        // runs at most a few times a day given CacheDuration.
        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        var response = await httpClient.GetFromJsonAsync<SetList>("sets", cancellationToken);
        var sets = response?.Data ?? [];

        return sets.Where(set => set is { Code: not null, Name: not null })
            .ToDictionary(set => set.Name!, set => set.Code!, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record SetList(
        [property: JsonPropertyName("data")] IReadOnlyList<SetSummary>? Data
    );

    private sealed record SetSummary(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("name")] string? Name
    );
}
