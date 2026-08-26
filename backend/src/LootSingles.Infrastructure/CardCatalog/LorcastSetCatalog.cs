using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LootSingles.Infrastructure.CardCatalog;

/// <summary>
/// App-lifetime cache of Lorcast's set name -> set code(s) list (research.md §5, mirroring
/// <see cref="TcgdexSetCatalog"/>'s design exactly - Lorcast's own guidance asks consumers to
/// cache bulk, slow-changing reference data locally rather than fetching it repeatedly).
/// Registered as a singleton so the cache is shared across every request, not just within one.
/// </summary>
public sealed partial class LorcastSetCatalog(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider
)
{
    private const string HttpClientName = "lorcast";

    // TCGplayer collapses every Lorcana promo drop under one generic label that matches no single
    // real Lorcast set name (confirmed live, research.md §5 update) - Lorcast instead publishes
    // each drop as its own separate, numbered set. The same collector number can appear in more
    // than one of these with a different real card, so every promo-shaped set is offered as a
    // candidate under this synthetic key and name verification (CardNameMatcher) disambiguates,
    // exactly like ScryfallCardCatalogProvider's multi-set-code candidates.
    private const string GenericPromoLabel = "Disney Lorcana Promo Cards";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    private readonly Lock _lock = new();
    private Task<IReadOnlyDictionary<string, IReadOnlyList<string>>>? _setCodesByNameTask;
    private DateTimeOffset _fetchedAt;

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetSetCodesByNameAsync(
        CancellationToken cancellationToken
    )
    {
        Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> task;
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

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> FetchSetCodesByNameAsync(
        CancellationToken cancellationToken
    )
    {
        // A fresh client per fetch (rather than one held for the singleton's lifetime) so
        // IHttpClientFactory's normal handler rotation/DNS-refresh behavior is unaffected - this
        // runs at most a few times a day given CacheDuration, so there's no pooling benefit lost.
        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        var document = await httpClient.GetFromJsonAsync<SetListResponse>(
            "sets",
            cancellationToken
        );
        var validSets = (document?.Results ?? [])
            .Where(set => set is { Code: not null, Name: not null })
            .ToArray();

        var setCodesByName = validSets.ToDictionary(
            set => set.Name!,
            set => (IReadOnlyList<string>)[set.Code!],
            StringComparer.OrdinalIgnoreCase
        );

        var promoSetCodes = validSets
            .Where(set => PromoSetNamePattern().IsMatch(set.Name!))
            .Select(set => set.Code!)
            .ToArray();
        if (promoSetCodes.Length > 0)
        {
            setCodesByName[GenericPromoLabel] = promoSetCodes;
        }

        return setCodesByName;
    }

    [GeneratedRegex(@"^(Promo Set \d+|Challenge Promo)$")]
    private static partial Regex PromoSetNamePattern();

    private sealed record SetListResponse(
        [property: JsonPropertyName("results")] IReadOnlyList<SetSummary>? Results
    );

    private sealed record SetSummary(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("name")] string? Name
    );
}
