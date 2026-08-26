using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LootSingles.Application.CardCatalog;

namespace LootSingles.Infrastructure.CardCatalog;

/// <summary>
/// <see cref="ICardCatalogProvider"/> for Pokémon, backed by TCGdex (https://api.tcgdex.net/v2).
/// Switched from the Pokémon TCG API (research.md §4 "Update — provider switched to TCGdex"):
/// live testing found the Pokémon TCG API failing roughly half of all requests regardless of
/// concurrency, while TCGdex handled the same 50-concurrent-request load with 100% success.
///
/// TCGdex identifies sets by short id (e.g. "sv10.5b"), not by name, so a card lookup is two
/// calls: resolve the packing slip's (normalized) set name to a set id via <see cref="TcgdexSetCatalog"/>
/// (an app-lifetime cache shared across requests — research.md §12), then fetch the card directly
/// by set id + collector number (a unique lookup — TCGdex has no multiple-candidate-match case the
/// way a text search would).
/// </summary>
public sealed class TcgdexCardCatalogProvider(HttpClient httpClient, TcgdexSetCatalog setCatalog)
    : ICardCatalogProvider
{
    public string ProductLine => "Pokemon";

    public async Task<string?> TryMatchImageUrlAsync(
        CardIdentity identity,
        CancellationToken cancellationToken
    )
    {
        var setIdsByName = await setCatalog.GetSetIdsByNameAsync(cancellationToken);
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
        // (Full Art)") that the provider's own card name never includes ("Alakazam V"), and may
        // disagree with the provider's diacritics for the same real card. CardNameMatcher strips
        // only a *trailing* parenthetical and ignores diacritics for this comparison — not a
        // fuzzy match, just excluding text already structurally recognized elsewhere
        // (OrderLineExtractor's own parenthetical marker extraction) as a printing/variant marker.
        if (!CardNameMatcher.Matches(identity.ProductName, card.Name))
        {
            return null;
        }

        // PRD §32 step 9 / FR-002: validate printing/variant information where obtainable.
        // Asymmetric and conservative by design (research.md): only reject when the packing
        // slip's Variant text explicitly claims a finish TCGdex says is impossible for this
        // exact print - never the reverse (a silent Variant is never treated as implying
        // "normal"), since that inference is weaker and would risk false rejections.
        if (VariantConflictsWithTcgdexVariants(identity.Variant, card.Variants))
        {
            return null;
        }

        return $"{card.Image}/high.webp";
    }

    private static bool VariantConflictsWithTcgdexVariants(string? variant, Variants? variants)
    {
        if (variant is null || variants is null)
        {
            return false;
        }

        // "Reverse Holofoil" must be checked before plain "Holofoil" - it contains "Holofoil"
        // as a substring and would otherwise be misread as a plain-holo claim.
        if (variant.Contains("Reverse Holofoil", StringComparison.OrdinalIgnoreCase))
        {
            return variants.Reverse == false;
        }

        if (variant.Contains("Holofoil", StringComparison.OrdinalIgnoreCase))
        {
            return variants.Holo == false;
        }

        if (variant.Contains("1st Edition", StringComparison.OrdinalIgnoreCase))
        {
            return variants.FirstEdition == false;
        }

        return false;
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

    private sealed record Card(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("image")] string? Image,
        [property: JsonPropertyName("variants")] Variants? Variants
    );

    private sealed record Variants(
        [property: JsonPropertyName("holo")] bool? Holo,
        [property: JsonPropertyName("reverse")] bool? Reverse,
        [property: JsonPropertyName("firstEdition")] bool? FirstEdition
    );
}
