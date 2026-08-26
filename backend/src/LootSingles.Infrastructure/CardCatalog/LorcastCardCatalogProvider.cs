using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LootSingles.Application.CardCatalog;
using Microsoft.Extensions.Logging;

namespace LootSingles.Infrastructure.CardCatalog;

/// <summary>
/// <see cref="ICardCatalogProvider"/> for Disney Lorcana, backed by Lorcast (https://api.lorcast.com/v0).
///
/// Lorcast identifies sets by short code (e.g. "1"), not by name, so a card lookup is two calls:
/// resolve the packing slip's set name to a set code via <see cref="LorcastSetCatalog"/> (an
/// app-lifetime cache shared across requests, mirroring <see cref="TcgdexSetCatalog"/>), then fetch
/// the card directly by set code + collector number (a unique lookup - Lorcast has no
/// multiple-candidate-match case the way a text search would, same as TCGdex).
///
/// Unlike TCGdex, the image URL is used verbatim from the response (<c>image_uris.digital.large</c>)
/// rather than constructed - Lorcast's own docs explicitly warn against assuming the CDN's URL
/// structure or domain will stay fixed (research.md §5).
///
/// Unlike TCGdex (no published hard rate limit) and Scryfall (has a batch endpoint), Lorcast
/// documents a real, enforced ~10 requests/second limit with no batch/collection endpoint to fall
/// back on. <see cref="TryMatchImageUrlsAsync"/> is overridden to gate per-identity requests
/// through a <see cref="SemaphoreSlim"/> rather than reuse the interface's unthrottled default fan-
/// out, which is safe for TCGdex only because TCGdex has no hard limit (research.md §5).
/// </summary>
public sealed class LorcastCardCatalogProvider(
    HttpClient httpClient,
    LorcastSetCatalog setCatalog,
    ILogger<LorcastCardCatalogProvider> logger
) : ICardCatalogProvider
{
    // Safely under Lorcast's documented ~10 requests/second guidance; a concurrency cap is an
    // approximation of a requests-per-second limit, not an exact match, but self-paces reasonably
    // well since each real HTTP request carries real latency (research.md §5's semaphore decision).
    private const int MaxConcurrentRequests = 5;

    public string ProductLine => "Lorcana TCG";

    public async Task<string?> TryMatchImageUrlAsync(
        CardIdentity identity,
        CancellationToken cancellationToken
    )
    {
        var setCodesByName = await setCatalog.GetSetCodesByNameAsync(cancellationToken);
        if (!setCodesByName.TryGetValue(identity.Set, out var setCodes) || setCodes.Count == 0)
        {
            return null;
        }

        var collectorNumber = NormalizeCollectorNumber(identity.CollectorNumber);
        var validImages = new List<string>();
        foreach (var setCode in setCodes)
        {
            using var response = await httpClient.GetAsync(
                $"cards/{setCode}/{collectorNumber}",
                cancellationToken
            );
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            var card = await response.Content.ReadFromJsonAsync<Card>(cancellationToken);
            var imageUrl = card?.ImageUris?.Digital?.Large;
            if (card?.Name is null || imageUrl is null)
            {
                continue;
            }

            // Lorcast splits a card's printed name into separate "name" and "version" fields
            // (e.g. "Scrooge McDuck" / "S.H.U.S.H. Agent"), but TCGplayer's ProductName is the
            // combined "Name - Version" form - confirmed live and by the real fixture
            // OrderLineExtractionTests.cs already checks in. CardNameMatcher then strips a
            // trailing printing/variant descriptor and ignores diacritics for this comparison
            // only, same as every other provider (never a fuzzy match).
            var providerName = string.IsNullOrEmpty(card.Version)
                ? card.Name
                : $"{card.Name} - {card.Version}";
            if (CardNameMatcher.Matches(identity.ProductName, providerName))
            {
                validImages.Add(imageUrl);
            }
        }

        return CandidateImageResolver.Resolve(
            validImages,
            count =>
                logger.LogWarning(
                    "Lorcast lookup was ambiguous for set {SetName} and collector number {CollectorNumber}; {ValidCandidateCount} candidates were valid.",
                    identity.Set,
                    collectorNumber,
                    count
                )
        );
    }

    public async Task<IReadOnlyDictionary<CardIdentity, string?>> TryMatchImageUrlsAsync(
        IReadOnlyList<CardIdentity> identities,
        CancellationToken cancellationToken
    )
    {
        var distinctIdentities = identities.Distinct().ToArray();
        using var throttle = new SemaphoreSlim(MaxConcurrentRequests);
        var results = await Task.WhenAll(
            distinctIdentities.Select(async identity =>
            {
                await throttle.WaitAsync(cancellationToken);
                try
                {
                    return (
                        identity,
                        url: await TryMatchImageUrlAsync(identity, cancellationToken)
                    );
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return (identity, url: (string?)null);
                }
                finally
                {
                    throttle.Release();
                }
            })
        );

        return results.ToDictionary(r => r.identity, r => r.url);
    }

    private static string NormalizeCollectorNumber(string collectorNumber) =>
        collectorNumber.TrimStart('#').Split('/')[0];

    private sealed record Card(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("image_uris")] ImageUris? ImageUris
    );

    private sealed record ImageUris(
        [property: JsonPropertyName("digital")] DigitalImageUris? Digital
    );

    private sealed record DigitalImageUris([property: JsonPropertyName("large")] string? Large);
}
