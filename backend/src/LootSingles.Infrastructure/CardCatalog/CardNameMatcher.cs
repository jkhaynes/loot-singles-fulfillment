using System.Globalization;
using System.Text;

namespace LootSingles.Infrastructure.CardCatalog;

/// <summary>
/// Compares an imported ProductName against a catalog provider's own card name for
/// name-verification purposes only - never alters the displayed, authoritative ProductName
/// itself. Strips a trailing printing/variant descriptor (see
/// <see cref="TrailingParentheticalStripper"/>) and ignores diacritics, since TCGplayer's
/// packing-slip text and a provider's canonical name have been observed to disagree on
/// accenting the same real card (e.g. "Jotun Grunt" vs. "Jötun Grunt") without that
/// representing a different card.
/// </summary>
public static class CardNameMatcher
{
    public static bool Matches(string productName, string providerCardName)
    {
        var comparableProductName = TrailingParentheticalStripper.Strip(productName);
        return string.Equals(
            RemoveDiacritics(comparableProductName),
            RemoveDiacritics(providerCardName),
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static string RemoveDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
