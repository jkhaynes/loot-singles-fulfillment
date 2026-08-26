using System.Text.RegularExpressions;

namespace LootSingles.Infrastructure.CardCatalog;

/// <summary>
/// Produces candidate spellings of an imported set name for crosswalk lookup only - never
/// alters the displayed, authoritative Set value itself. PDF text extraction can turn a
/// mid-word line-wrapped hyphen (e.g. "Middle-earth" broken across a line) into a hyphen
/// followed by a stray space ("Middle- earth"), which does not match any known Scryfall set
/// name. That artifact shape - a hyphen with no space *before* it but a space *after* it - is
/// distinct from a genuine spaced dash separator (e.g. "Foo - Bar"), which has a space on
/// both sides and is left untouched.
/// </summary>
public static partial class HyphenatedSetNameNormalizer
{
    public static IReadOnlyList<string> NormalizeCandidates(string setName)
    {
        var collapsed = Pattern().Replace(setName, "-");
        return string.Equals(collapsed, setName, StringComparison.Ordinal)
            ? [setName]
            : [setName, collapsed];
    }

    [GeneratedRegex(@"(?<=\S)-\s+")]
    private static partial Regex Pattern();
}
