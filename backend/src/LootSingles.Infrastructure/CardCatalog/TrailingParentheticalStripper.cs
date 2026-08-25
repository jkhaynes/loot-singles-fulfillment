using System.Text.RegularExpressions;

namespace LootSingles.Infrastructure.CardCatalog;

/// <summary>
/// Strips a trailing printing/variant descriptor (e.g. "(Full Art)", "(Borderless)") from an
/// imported product name for name-verification comparison only - never alters the displayed,
/// authoritative ProductName itself. Shared across providers since TCGplayer's own product-name
/// formatting convention (a trailing parenthetical the provider's own card name never includes)
/// is provider-independent: confirmed for both Pokemon (TCGdex, research.md §4 - e.g. "Alakazam V
/// (Full Art)") and Magic (Scryfall, research.md §3 - e.g. "Galadriel's Dismissal (Borderless)").
/// Reuses the same parenthetical shape OrderLineExtractor (feature 001) already recognizes as a
/// printing/variant marker for Variant parsing, not an invented new concept.
/// </summary>
public static partial class TrailingParentheticalStripper
{
    public static string Strip(string name) => Pattern().Replace(name, "").TrimEnd();

    [GeneratedRegex(@"\s*\([^()]+\)$")]
    private static partial Regex Pattern();
}
