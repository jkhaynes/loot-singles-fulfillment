namespace LootSingles.Application.CardCatalog;

public interface ICardCatalogProvider
{
    string ProductLine { get; }

    Task<string?> TryMatchImageUrlAsync(CardIdentity identity, CancellationToken cancellationToken);
}
