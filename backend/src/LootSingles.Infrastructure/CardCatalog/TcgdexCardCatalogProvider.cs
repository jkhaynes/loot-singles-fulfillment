using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LootSingles.Application.CardCatalog;

namespace LootSingles.Infrastructure.CardCatalog;

/// <summary>
/// <see cref="ICardCatalogProvider"/> for Pokémon, backed by TCGdex (https://api.tcgdex.net/v2).
/// Switched from the Pokémon TCG API (research.md §4 "Update — provider switched to TCGdex"):
/// live testing found the Pokémon TCG API failing roughly half of all requests regardless of
/// concurrency, while TCGdex handled the same 50-concurrent-request load with 100% success.
///
/// TCGdex identifies sets by short id (e.g. "sv10.5b"), not by name, so a card lookup is two
/// calls: resolve the packing slip's (normalized) set name to a set id via the sets list, then
/// fetch the card directly by set id + collector number (a unique lookup — TCGdex has no
/// multiple-candidate-match case the way a text search would). The sets list rarely changes, so
/// it's cached for the lifetime of this instance (one instance is reused for every line in a
/// single order-detail request — see CardImageEnrichmentService's DI scope) rather than re-fetched
/// per line.
/// </summary>
public sealed partial class TcgdexCardCatalogProvider(HttpClient httpClient) : ICardCatalogProvider
{
    public string ProductLine => "Pokemon";

    private readonly Lock _setsLock = new();
    private Task<IReadOnlyDictionary<string, string>>? _setIdsByNameTask;

    public async Task<string?> TryMatchImageUrlAsync(
        CardIdentity identity,
        CancellationToken cancellationToken
    )
    {
        var setIdsByName = await GetSetIdsByNameAsync(cancellationToken);
        var setId = PokemonSeriesPrefixNormalizer
            .NormalizeCandidates(identity.Set)
            .Select(candidate => setIdsByName.TryGetValue(candidate, out var id) ? id : null)
            .FirstOrDefault(id => id is not null);
        if (setId is null)
        {
            return null;
        }

        var collectorNumber = NormalizeCollectorNumber(identity.CollectorNumber);
        using var response = await httpClient.GetAsync(
            $"sets/{setId}/{collectorNumber}",
            cancellationToken
        );
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var card = await response.Content.ReadFromJsonAsync<Card>(cancellationToken);
        if (card?.Name is null || card.Image is null)
        {
            return null;
        }

        // The imported ProductName is authoritative and kept as-is for display, but TCGplayer
        // sometimes appends a trailing printing/variant descriptor to it (e.g. "Alakazam V
        // (Full Art)") that the provider's own card name never includes ("Alakazam V"). Strip
        // only a *trailing* parenthetical for this comparison — not a fuzzy match, just excluding
        // text already structurally recognized elsewhere (OrderLineExtractor's own parenthetical
        // marker extraction) as a printing/variant marker rather than part of the card's name.
        var comparableProductName = StripTrailingParenthetical(identity.ProductName);
        if (!string.Equals(card.Name, comparableProductName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"{card.Image}/high.webp";
    }

    private static string StripTrailingParenthetical(string name) =>
        TrailingParentheticalPattern().Replace(name, "").TrimEnd();

    [GeneratedRegex(@"\s*\([^()]+\)$")]
    private static partial Regex TrailingParentheticalPattern();

    private Task<IReadOnlyDictionary<string, string>> GetSetIdsByNameAsync(
        CancellationToken cancellationToken
    )
    {
        lock (_setsLock)
        {
            _setIdsByNameTask ??= FetchSetIdsByNameAsync(cancellationToken);
        }

        return _setIdsByNameTask;
    }

    private async Task<IReadOnlyDictionary<string, string>> FetchSetIdsByNameAsync(
        CancellationToken cancellationToken
    )
    {
        var sets =
            await httpClient.GetFromJsonAsync<IReadOnlyList<SetSummary>>("sets", cancellationToken)
            ?? [];

        return sets.Where(set => set is { Id: not null, Name: not null })
            .ToDictionary(set => set.Name!, set => set.Id!, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeCollectorNumber(string collectorNumber)
    {
        // TCGdex is inconsistent about accepting a zero-padded local number: some sets accept
        // both "5" and "005" for the same card, but others (confirmed live: Celebrations,
        // Evolving Skies) 404 on the padded form and only resolve the unpadded one. Stripping
        // leading zeros is safe everywhere observed, so always do it — a purely-numeric prefix
        // like "085" becomes "85"; an alphanumeric one like "TG02" is unaffected (it doesn't
        // start with '0').
        var withoutHashAndTotal = collectorNumber.TrimStart('#').Split('/')[0];
        var withoutLeadingZeros = withoutHashAndTotal.TrimStart('0');
        return withoutLeadingZeros.Length == 0 ? "0" : withoutLeadingZeros;
    }

    private sealed record SetSummary(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name
    );

    private sealed record Card(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("image")] string? Image
    );
}
