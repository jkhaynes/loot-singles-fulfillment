using System.Reflection;
using System.Text.Json;

namespace LootSingles.Infrastructure.CardCatalog;

/// <summary>
/// Strips a recognized Pokémon series-abbreviation prefix (e.g. "SV: ", "SV10: ", "ME05: ")
/// from a packing slip's raw Set text, so the remainder matches the Pokémon TCG API's real
/// set.name (research.md §4: "SV: Black Bolt" -> "Black Bolt", confirmed live). Deliberately
/// keyed by a small, series-level abbreviation table (Data/pokemon-series-abbreviations.json)
/// rather than a per-set/per-group table, so it only needs updating when Pokémon starts a new
/// naming era, not with every new set release. An unrecognized prefix is left unchanged.
/// </summary>
public static class PokemonSeriesPrefixNormalizer
{
    private const string ResourceName =
        "LootSingles.Infrastructure.CardCatalog.Data.pokemon-series-abbreviations.json";

    private static readonly IReadOnlyList<string> KnownAbbreviations = LoadAbbreviations();

    public static string Normalize(string rawSetText) =>
        MatchAbbreviation(rawSetText)?.Name ?? rawSetText;

    private static (string Abbreviation, string Name)? MatchAbbreviation(string rawSetText)
    {
        foreach (var abbreviation in KnownAbbreviations)
        {
            if (!rawSetText.StartsWith(abbreviation, StringComparison.Ordinal))
            {
                continue;
            }

            var afterAbbreviation = rawSetText.AsSpan(abbreviation.Length);
            var digitCount = 0;
            while (
                digitCount < afterAbbreviation.Length
                && char.IsAsciiDigit(afterAbbreviation[digitCount])
            )
            {
                digitCount++;
            }

            var afterDigits = afterAbbreviation[digitCount..];
            if (afterDigits.StartsWith(": ", StringComparison.Ordinal))
            {
                return (abbreviation, afterDigits[2..].ToString());
            }
        }

        return null;
    }

    /// <summary>
    /// Ordered set-name candidates to try against a provider's own set list: the abbreviation-
    /// stripped result of <see cref="Normalize"/> first, then — only when that differs from the
    /// raw text and the raw text has a colon-delimited prefix that ISN'T a known series
    /// abbreviation (e.g. "Celebrations: Classic Collection", where "Celebrations" is itself a
    /// real, standalone set name used as a sub-collection prefix, not a short era code) — a
    /// second candidate with the first ": " replaced by a plain space (e.g.
    /// "Celebrations Classic Collection", confirmed to match TCGdex's real name for this case).
    /// Never duplicates a candidate already produced.
    /// </summary>
    public static IReadOnlyList<string> NormalizeCandidates(string rawSetText)
    {
        var match = MatchAbbreviation(rawSetText);
        var normalized = match?.Name ?? rawSetText;
        var candidates = new List<string> { normalized };

        // TCGplayer labels Black Star Promo sets as "<full era name> Promo[ Cards]" (e.g. "Mega
        // Evolution Promo"), but TCGdex names them "<code> Black Star Promos" instead - and,
        // confirmed live against TCGdex's full sets list, the code is sometimes the plain era
        // abbreviation (e.g. "SWSH") and sometimes has a "P" appended (e.g. "SVP", "MEP") with no
        // rule that predicts which. Try both forms, built from the abbreviation this method
        // already recognized - each is only ever used if it exactly matches a real entry in
        // TCGdex's own fetched set list, so a wrong guess simply fails to match.
        if (
            match is { } recognized
            && (
                normalized.EndsWith(" Promo", StringComparison.Ordinal)
                || normalized.EndsWith(" Promo Cards", StringComparison.Ordinal)
            )
        )
        {
            candidates.Add($"{recognized.Abbreviation} Black Star Promos");
            candidates.Add($"{recognized.Abbreviation}P Black Star Promos");
        }

        var colonIndex = rawSetText.IndexOf(": ", StringComparison.Ordinal);
        // Only offer the colon-to-space fallback when no known abbreviation prefix was
        // recognized — a recognized abbreviation already produced the correct candidate, and
        // trying a second, unverified guess alongside it would risk a wrong match.
        if (colonIndex >= 0 && normalized == rawSetText)
        {
            var colonToSpace = string.Concat(
                rawSetText.AsSpan(0, colonIndex),
                " ",
                rawSetText.AsSpan(colonIndex + 2)
            );
            if (!candidates.Contains(colonToSpace, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(colonToSpace);
            }
        }

        return candidates;
    }

    private static IReadOnlyList<string> LoadAbbreviations()
    {
        var assembly = typeof(PokemonSeriesPrefixNormalizer).GetTypeInfo().Assembly;
        using var stream =
            assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' was not found."
            );
        using var document = JsonDocument.Parse(stream);

        return document
            .RootElement.GetProperty("series")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("abbreviation").GetString()!)
            // Longest-first so a future abbreviation that happens to prefix another (e.g. "S" and
            // "SM") can't shadow the more specific match.
            .OrderByDescending(abbreviation => abbreviation.Length)
            .ToArray();
    }
}
