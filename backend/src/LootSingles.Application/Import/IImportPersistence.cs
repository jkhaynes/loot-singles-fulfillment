using LootSingles.Domain.Orders;

namespace LootSingles.Application.Import;

public interface IImportPersistence
{
    void AddImportAttempt(ImportAttempt attempt);

    void AddOrder(Order order);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
