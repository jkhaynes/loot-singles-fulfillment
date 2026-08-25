using Microsoft.Extensions.Logging;

namespace LootSingles.Application.CardCatalog;

public sealed class CardImageEnrichmentService(
    IEnumerable<ICardCatalogProvider> providers,
    ILogger<CardImageEnrichmentService> logger
)
{
    public async Task<string?> TryGetImageUrlAsync(
        string productLine,
        CardIdentity identity,
        CancellationToken cancellationToken
    )
    {
        var provider = providers.FirstOrDefault(candidate =>
            string.Equals(candidate.ProductLine, productLine, StringComparison.OrdinalIgnoreCase)
        );
        if (provider is null)
        {
            return null;
        }

        try
        {
            return await provider.TryMatchImageUrlAsync(identity, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Card catalog provider for product line {ProductLine} failed to resolve an image.",
                productLine
            );
            return null;
        }
    }
}
