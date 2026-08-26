using Microsoft.Extensions.Logging;

namespace LootSingles.Application.CardCatalog;

public sealed class CardImageEnrichmentService(
    IEnumerable<ICardCatalogProvider> providers,
    ILogger<CardImageEnrichmentService> logger
)
{
    public async Task<IReadOnlyDictionary<CardIdentity, string?>> TryGetImageUrlsAsync(
        string productLine,
        IReadOnlyList<CardIdentity> identities,
        CancellationToken cancellationToken
    )
    {
        var provider = providers.FirstOrDefault(candidate =>
            string.Equals(candidate.ProductLine, productLine, StringComparison.OrdinalIgnoreCase)
        );
        if (provider is null)
        {
            return NullResultFor(identities);
        }

        try
        {
            return await provider.TryMatchImageUrlsAsync(identities, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Card catalog provider for product line {ProductLine} failed to resolve images for {Count} card(s).",
                productLine,
                identities.Count
            );
            return NullResultFor(identities);
        }
    }

    private static IReadOnlyDictionary<CardIdentity, string?> NullResultFor(
        IReadOnlyList<CardIdentity> identities
    ) => identities.Distinct().ToDictionary(identity => identity, _ => (string?)null);
}
