namespace LootSingles.Infrastructure.CardCatalog;

/// <summary>
/// Shared "exactly one valid candidate" safety check: given the image URLs of every candidate
/// that independently passed a provider's own match verification for one card identity, returns
/// that image only when precisely one candidate was valid. Two or more valid candidates means the
/// identity is genuinely ambiguous between real, distinct results (e.g. Scryfall's multi-set-code
/// crosswalk entries, Lorcast's promo sets reusing collector numbers across different cards) - the
/// caller's <paramref name="logAmbiguous"/> callback is invoked with the count so it can log with
/// its own provider-specific context, and this method returns <c>null</c> rather than guessing,
/// per Principle V ("no image is better than the wrong image"). Zero valid candidates also
/// returns <c>null</c>, without logging - that is the ordinary, unremarkable "no match" outcome.
///
/// Not used by <see cref="TcgdexCardCatalogProvider"/>: TCGdex's set-name candidates are alternate
/// spellings of one intended set, not alternate real sets, so at most one can ever resolve - it
/// has no genuine multi-candidate-ambiguity case to guard against (research.md).
/// </summary>
public static class CandidateImageResolver
{
    public static string? Resolve(IReadOnlyList<string> validImages, Action<int> logAmbiguous)
    {
        if (validImages.Count > 1)
        {
            logAmbiguous(validImages.Count);
            return null;
        }

        return validImages.Count == 1 ? validImages[0] : null;
    }
}
