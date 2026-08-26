using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LootSingles.Application.CardCatalog;
using Microsoft.Extensions.Logging;

namespace LootSingles.Infrastructure.CardCatalog;

public sealed class ScryfallCardCatalogProvider(
    HttpClient httpClient,
    IMagicSetCrosswalk setCrosswalk,
    ILogger<ScryfallCardCatalogProvider> logger
) : ICardCatalogProvider
{
    private const int MaxIdentifiersPerRequest = 75;

    // Some Unfinity "Attraction" cards print a letter-suffixed collector number (e.g. "222a")
    // that TCGplayer's packing slip prints without the letter ("#222"). Not universal - other
    // Attractions have a plain number - and some base numbers have multiple distinct real prints
    // sharing it (e.g. Scavenger Hunt: 226a/226b/226c), so this can never assume a single letter;
    // it must go through the same "exactly one valid candidate" safety check as everything else.
    private static readonly string[] AttractionLetterSuffixes = ["a", "b", "c"];

    public string ProductLine => "Magic";

    public async Task<string?> TryMatchImageUrlAsync(
        CardIdentity identity,
        CancellationToken cancellationToken
    )
    {
        var results = await TryMatchImageUrlsAsync([identity], cancellationToken);
        return results.TryGetValue(identity, out var url) ? url : null;
    }

    public async Task<IReadOnlyDictionary<CardIdentity, string?>> TryMatchImageUrlsAsync(
        IReadOnlyList<CardIdentity> identities,
        CancellationToken cancellationToken
    )
    {
        var distinctIdentities = identities.Distinct().ToArray();
        var results = distinctIdentities.ToDictionary(identity => identity, _ => (string?)null);
        var setCodesByIdentity = new Dictionary<CardIdentity, IReadOnlyList<string>>();
        var candidates = new List<Candidate>();

        foreach (var identity in distinctIdentities)
        {
            var setCodes = ResolveSetCodes(identity);
            if (setCodes is null || setCodes.Count == 0)
            {
                continue;
            }

            setCodesByIdentity[identity] = setCodes;
            candidates.AddRange(
                setCodes.Select(code => new Candidate(
                    identity,
                    code,
                    NormalizeCollectorNumber(identity.CollectorNumber)
                ))
            );
        }

        var returnedCards = await FetchAllAsync(candidates, cancellationToken);
        var unresolvedIdentities = new List<CardIdentity>();
        foreach (var identityGroup in candidates.GroupBy(candidate => candidate.Identity))
        {
            var validImages = EvaluateCandidates(identityGroup, returnedCards);
            var resolved = CandidateImageResolver.Resolve(
                validImages,
                count => LogAmbiguous(identityGroup.Key, count)
            );
            if (resolved is not null)
            {
                results[identityGroup.Key] = resolved;
            }
            else if (validImages.Count == 0)
            {
                unresolvedIdentities.Add(identityGroup.Key);
            }
        }

        // Only retry with a letter-suffixed collector number for identities that found zero
        // valid matches with their base number - trying every identity would quadruple query
        // volume for every Magic card just to handle this rare Unfinity-specific quirk.
        var suffixCandidates = unresolvedIdentities
            .Where(setCodesByIdentity.ContainsKey)
            .SelectMany(identity =>
                setCodesByIdentity[identity]
                    .SelectMany(code =>
                        AttractionLetterSuffixes.Select(suffix => new Candidate(
                            identity,
                            code,
                            NormalizeCollectorNumber(identity.CollectorNumber) + suffix
                        ))
                    )
            )
            .ToList();

        if (suffixCandidates.Count > 0)
        {
            var suffixCards = await FetchAllAsync(suffixCandidates, cancellationToken);
            foreach (var identityGroup in suffixCandidates.GroupBy(candidate => candidate.Identity))
            {
                var validImages = EvaluateCandidates(identityGroup, suffixCards);
                var resolved = CandidateImageResolver.Resolve(
                    validImages,
                    count => LogAmbiguous(identityGroup.Key, count)
                );
                if (resolved is not null)
                {
                    results[identityGroup.Key] = resolved;
                }
            }
        }

        return results;
    }

    private IReadOnlyList<string>? ResolveSetCodes(CardIdentity identity)
    {
        var setCodes = HyphenatedSetNameNormalizer
            .NormalizeCandidates(identity.Set)
            .Select(candidateSetName =>
                setCrosswalk.TryGetScryfallSetCodes(candidateSetName, out var codes) ? codes : null
            )
            .FirstOrDefault(codes => codes is not null);

        if (setCodes is null)
        {
            logger.LogInformation(
                "No TCGplayer-to-Scryfall Magic set mapping exists for set {SetName}.",
                identity.Set
            );
            return null;
        }

        if (setCodes.Count == 0)
        {
            logger.LogInformation(
                "TCGplayer-to-Scryfall Magic set mapping has no candidate codes for set {SetName}.",
                identity.Set
            );
        }

        return setCodes;
    }

    private async Task<List<Card>> FetchAllAsync(
        List<Candidate> candidates,
        CancellationToken cancellationToken
    )
    {
        var returnedCards = new List<Card>();
        foreach (var chunk in candidates.Chunk(MaxIdentifiersPerRequest))
        {
            returnedCards.AddRange(await FetchChunkAsync(chunk, cancellationToken));
        }

        return returnedCards;
    }

    private List<string> EvaluateCandidates(
        IEnumerable<Candidate> candidatesForIdentity,
        IReadOnlyList<Card> returnedCards
    )
    {
        var validImages = new List<string>();
        foreach (var candidate in candidatesForIdentity)
        {
            var candidateImages = returnedCards
                .Where(card =>
                    string.Equals(card.Set, candidate.SetCode, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        card.CollectorNumber,
                        candidate.CollectorNumber,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && CardNameMatches(candidate.Identity.ProductName, card)
                    && !VariantConflictsWithFinishes(candidate.Identity.Variant, card.Finishes)
                )
                .Select(GetImageUrl)
                .Where(url => url is not null)
                .Cast<string>()
                .ToArray();

            if (candidateImages.Length == 1)
            {
                validImages.Add(candidateImages[0]);
            }
            else
            {
                logger.LogInformation(
                    "Scryfall candidate {SetCode} failed safe for collector number {CollectorNumber}; valid card count was {ValidCardCount}.",
                    candidate.SetCode,
                    candidate.CollectorNumber,
                    candidateImages.Length
                );
            }
        }

        return validImages;
    }

    private void LogAmbiguous(CardIdentity identity, int validCandidateCount) =>
        logger.LogWarning(
            "Scryfall lookup was ambiguous for Magic set {SetName} and collector number {CollectorNumber}; {ValidCandidateCount} candidates were valid.",
            identity.Set,
            NormalizeCollectorNumber(identity.CollectorNumber),
            validCandidateCount
        );

    private async Task<IReadOnlyList<Card>> FetchChunkAsync(
        Candidate[] chunk,
        CancellationToken cancellationToken
    )
    {
        var request = new CollectionRequest(
            chunk
                .Select(entry => new CardIdentifier(entry.SetCode, entry.CollectorNumber))
                .ToArray()
        );
        using var response = await httpClient.PostAsJsonAsync(
            "cards/collection",
            request,
            cancellationToken
        );
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Scryfall collection lookup failed with status {StatusCode} for {CandidateCount} candidate(s).",
                (int)response.StatusCode,
                chunk.Length
            );
            return [];
        }

        var cardList = await response.Content.ReadFromJsonAsync<CardList>(cancellationToken);
        return cardList?.Data ?? [];
    }

    private static bool VariantConflictsWithFinishes(
        string? variant,
        IReadOnlyList<string>? finishes
    )
    {
        // PRD §32 step 9 / FR-002: validate printing/variant information where obtainable.
        // Asymmetric and conservative by design (research.md): only reject when the packing
        // slip's Variant text explicitly claims "Foil" and Scryfall says this exact print has
        // no foil-type finish at all - never the reverse (a silent Variant is never treated as
        // implying "nonfoil"), since that inference is weaker and would risk false rejections.
        if (finishes is null || finishes.Count == 0)
        {
            return false;
        }

        var claimsFoil = variant?.Contains("Foil", StringComparison.OrdinalIgnoreCase) ?? false;
        if (!claimsFoil)
        {
            return false;
        }

        return finishes.All(finish =>
            string.Equals(finish, "nonfoil", StringComparison.OrdinalIgnoreCase)
        );
    }

    private static bool CardNameMatches(string productName, Card card)
    {
        var cardName = card.CardFaces?.FirstOrDefault()?.Name ?? card.Name;
        return cardName is not null && CardNameMatcher.Matches(productName, cardName);
    }

    private static string? GetImageUrl(Card card) =>
        card.ImageUris?.Large ?? card.CardFaces?.FirstOrDefault()?.ImageUris?.Large;

    private static string NormalizeCollectorNumber(string collectorNumber) =>
        collectorNumber.TrimStart('#').Split('/')[0];

    private sealed record Candidate(CardIdentity Identity, string SetCode, string CollectorNumber);

    private sealed record CollectionRequest(
        [property: JsonPropertyName("identifiers")] IReadOnlyList<CardIdentifier> Identifiers
    );

    private sealed record CardIdentifier(
        [property: JsonPropertyName("set")] string Set,
        [property: JsonPropertyName("collector_number")] string CollectorNumber
    );

    private sealed record CardList([property: JsonPropertyName("data")] IReadOnlyList<Card>? Data);

    private sealed record Card(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("set")] string? Set,
        [property: JsonPropertyName("collector_number")] string? CollectorNumber,
        [property: JsonPropertyName("image_uris")] ImageUris? ImageUris,
        [property: JsonPropertyName("card_faces")] IReadOnlyList<CardFace>? CardFaces,
        [property: JsonPropertyName("finishes")] IReadOnlyList<string>? Finishes
    );

    private sealed record CardFace(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("image_uris")] ImageUris? ImageUris
    );

    private sealed record ImageUris([property: JsonPropertyName("large")] string? Large);
}
