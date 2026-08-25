using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LootSingles.Application.CardCatalog;

namespace LootSingles.Infrastructure.CardCatalog;

/// <summary>
/// <see cref="ICardCatalogProvider"/> for Magic: The Gathering, backed by Scryfall
/// (https://api.scryfall.com). Unlike TCGdex, Scryfall enforces real hard rate limits (research.md
/// §3), so this provider overrides the batch operation to use <c>POST /cards/collection</c> (up
/// to 75 card identifiers per request) instead of one request per card - resolving an entire
/// order's Magic lines in one or a small handful of requests rather than one per line.
/// </summary>
public sealed class ScryfallCardCatalogProvider(
    HttpClient httpClient,
    ScryfallSetCatalog setCatalog
) : ICardCatalogProvider
{
    private const int MaxIdentifiersPerRequest = 75;

    public string ProductLine => "Magic";

    public async Task<string?> TryMatchImageUrlAsync(
        CardIdentity identity,
        CancellationToken cancellationToken
    )
    {
        var results = await TryMatchImageUrlsAsync([identity], cancellationToken);
        return results.TryGetValue(identity, out var url) ? url : null;
    }

    public async Task<IReadOnlyDictionary<CardIdentity, string?>> TryMatchImageUrlsAsync(
        IReadOnlyList<CardIdentity> identities,
        CancellationToken cancellationToken
    )
    {
        var distinctIdentities = identities.Distinct().ToArray();
        var results = distinctIdentities.ToDictionary(identity => identity, _ => (string?)null);

        var setCodesByName = await setCatalog.GetSetCodesByNameAsync(cancellationToken);

        var resolvable =
            new List<(CardIdentity Identity, string SetCode, string CollectorNumber)>();
        foreach (var identity in distinctIdentities)
        {
            var setCode = MagicSetNameNormalizer
                .NormalizeCandidates(identity.Set)
                .Select(candidate =>
                    setCodesByName.TryGetValue(candidate, out var code) ? code : null
                )
                .FirstOrDefault(code => code is not null);
            if (setCode is not null)
            {
                resolvable.Add(
                    (identity, setCode, NormalizeCollectorNumber(identity.CollectorNumber))
                );
            }
        }

        foreach (var chunk in resolvable.Chunk(MaxIdentifiersPerRequest))
        {
            var cards = await FetchChunkAsync(chunk, cancellationToken);
            foreach (var entry in chunk)
            {
                var card = cards.FirstOrDefault(candidate =>
                    string.Equals(candidate.Set, entry.SetCode, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        candidate.CollectorNumber,
                        entry.CollectorNumber,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
                if (card?.Name is null)
                {
                    continue;
                }

                // A multi-faced card (adventure/split/transform layout) reports a combined
                // top-level "name" (e.g. "My Precious // Allure of Power"), but the imported
                // ProductName is only the front face's name ("My Precious") - confirmed live.
                // Compare against the first face's name when present, the top-level name
                // otherwise.
                var cardName = card.CardFaces?.FirstOrDefault()?.Name ?? card.Name;
                var comparableProductName = TrailingParentheticalStripper.Strip(
                    entry.Identity.ProductName
                );
                if (
                    !string.Equals(
                        cardName,
                        comparableProductName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                var imageUrl =
                    card.ImageUris?.Large ?? card.CardFaces?.FirstOrDefault()?.ImageUris?.Large;
                if (imageUrl is not null)
                {
                    results[entry.Identity] = imageUrl;
                }
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<Card>> FetchChunkAsync(
        (CardIdentity Identity, string SetCode, string CollectorNumber)[] chunk,
        CancellationToken cancellationToken
    )
    {
        var request = new CollectionRequest(
            chunk
                .Select(entry => new CardIdentifier(entry.SetCode, entry.CollectorNumber))
                .ToArray()
        );
        using var response = await httpClient.PostAsJsonAsync(
            "cards/collection",
            request,
            cancellationToken
        );
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var cardList = await response.Content.ReadFromJsonAsync<CardList>(cancellationToken);
        return cardList?.Data ?? [];
    }

    private static string NormalizeCollectorNumber(string collectorNumber) =>
        collectorNumber.TrimStart('#').Split('/')[0];

    private sealed record CollectionRequest(
        [property: JsonPropertyName("identifiers")] IReadOnlyList<CardIdentifier> Identifiers
    );

    private sealed record CardIdentifier(
        [property: JsonPropertyName("set")] string Set,
        [property: JsonPropertyName("collector_number")] string CollectorNumber
    );

    private sealed record CardList([property: JsonPropertyName("data")] IReadOnlyList<Card>? Data);

    private sealed record Card(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("set")] string? Set,
        [property: JsonPropertyName("collector_number")] string? CollectorNumber,
        [property: JsonPropertyName("image_uris")] ImageUris? ImageUris,
        [property: JsonPropertyName("card_faces")] IReadOnlyList<CardFace>? CardFaces
    );

    private sealed record CardFace(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("image_uris")] ImageUris? ImageUris
    );

    private sealed record ImageUris([property: JsonPropertyName("large")] string? Large);
}
