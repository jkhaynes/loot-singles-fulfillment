namespace LootSingles.Infrastructure.CardCatalog;

/// <summary>
/// Ordered set-name candidates to try against Scryfall's own set list (research.md §3). Unlike
/// Pokémon's short-abbreviation-prefix pattern, most real Magic set names already contain a colon
/// (e.g. "Tarkir: Dragonstorm"), so a colon alone is not a signal that transformation is needed -
/// the majority of raw `Set` text already matches Scryfall's real set name directly. Three
/// narrowly-scoped, live-verified additional shapes are also offered as candidates: TCGplayer's
/// "Commander: X" product-line prefix is reversed and de-colonized on Scryfall's side ("X
/// Commander"); "Universes Beyond: X" has no Scryfall equivalent at all, only the suffix does;
/// ": Eternal-Legal" maps to a genuinely distinct real set named "X Eternal". Every candidate is
/// only ever used by the caller if it exactly matches a real entry already in Scryfall's own
/// fetched set-name dictionary, so an untested shape simply fails to match rather than risking a
/// wrong image.
/// </summary>
public static class MagicSetNameNormalizer
{
    private const string CommanderPrefix = "Commander: ";
    private const string UniversesBeyondPrefix = "Universes Beyond: ";
    private const string EternalLegalSuffix = ": Eternal-Legal";

    public static IReadOnlyList<string> NormalizeCandidates(string rawSetText)
    {
        var candidates = new List<string> { rawSetText };

        if (rawSetText.StartsWith(CommanderPrefix, StringComparison.Ordinal))
        {
            AddCandidate(candidates, $"{rawSetText[CommanderPrefix.Length..]} Commander");
        }

        if (rawSetText.StartsWith(UniversesBeyondPrefix, StringComparison.Ordinal))
        {
            AddCandidate(candidates, rawSetText[UniversesBeyondPrefix.Length..]);
        }

        if (rawSetText.EndsWith(EternalLegalSuffix, StringComparison.Ordinal))
        {
            AddCandidate(candidates, $"{rawSetText[..^EternalLegalSuffix.Length]} Eternal");
        }

        return candidates;
    }

    private static void AddCandidate(List<string> candidates, string candidate)
    {
        if (!candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(candidate);
        }
    }
}
