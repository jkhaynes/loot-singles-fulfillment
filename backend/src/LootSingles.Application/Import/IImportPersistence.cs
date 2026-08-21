using LootSingles.Domain.Orders;

namespace LootSingles.Application.Import;

public interface IImportPersistence
{
    void AddImportAttempt(ImportAttempt attempt);

    void AddOrder(Order order);

    void DiscardOrder(Order order);

    Task<bool> OrderExistsAsync(string tcgplayerOrderId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
