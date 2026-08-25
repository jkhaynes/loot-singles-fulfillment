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

    public static string Normalize(string rawSetText)
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
                return afterDigits[2..].ToString();
            }
        }

        return rawSetText;
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
