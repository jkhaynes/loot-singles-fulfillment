using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LootSingles.Application.CardCatalog;

namespace LootSingles.Infrastructure.CardCatalog;

/// <summary>
/// <see cref="ICardCatalogProvider"/> for Pokémon, backed by the Pokémon TCG API
/// (https://api.pokemontcg.io/v2/cards). Query shape confirmed in research.md §4: `set.name` is
/// directly queryable as text in the Lucene-like `q` parameter alongside `number`, so no
/// separate set-id lookup step is needed.
/// </summary>
public sealed class PokemonTcgApiCardCatalogProvider(HttpClient httpClient) : ICardCatalogProvider
{
    public string ProductLine => "Pokemon";

    public async Task<string?> TryMatchImageUrlAsync(
        CardIdentity identity,
        CancellationToken cancellationToken
    )
    {
        var collectorNumber = NormalizeCollectorNumber(identity.CollectorNumber);
        var set = PokemonSeriesPrefixNormalizer.Normalize(identity.Set);
        var query = $"""set.name:"{set}" number:{collectorNumber}""";
        var requestUri = $"cards?q={Uri.EscapeDataString(query)}";

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<SearchResponse>(cancellationToken);
        if (payload?.Data is not { Count: 1 } cards)
        {
            return null;
        }

        var card = cards[0];
        if (
            card.Name is null
            || !string.Equals(card.Name, identity.ProductName, StringComparison.OrdinalIgnoreCase)
        )
        {
            return null;
        }

        return card.Images?.Large;
    }

    private static string NormalizeCollectorNumber(string collectorNumber)
    {
        // The Pokémon TCG API stores `number` without leading zeros (e.g. "67", not "067"),
        // while the packing slip's CollectorNumber is zero-padded (e.g. "#067/086").
        var withoutHashAndTotal = collectorNumber.TrimStart('#').Split('/')[0];
        var withoutLeadingZeros = withoutHashAndTotal.TrimStart('0');
        return withoutLeadingZeros.Length == 0 ? "0" : withoutLeadingZeros;
    }

    private sealed record SearchResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<Card>? Data
    );

    private sealed record Card(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("images")] CardImages? Images
    );

    private sealed record CardImages([property: JsonPropertyName("large")] string? Large);
}
