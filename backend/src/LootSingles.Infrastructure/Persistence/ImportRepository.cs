using LootSingles.Application.Import;
using LootSingles.Application.Persistence;
using LootSingles.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.Infrastructure.Persistence;

/// <summary>
/// <see cref="IImportPersistence"/> over <see cref="LootSinglesDbContext"/>, mirroring the shape of
/// <see cref="EmployeeRepository"/> and <see cref="DashboardRepository"/> rather than the DbContext
/// implementing the interface directly.
/// </summary>
public sealed class ImportRepository(LootSinglesDbContext context) : IImportPersistence
{
    public void AddImportAttempt(ImportAttempt attempt) => context.ImportAttempts.Add(attempt);

    public void AddOrder(Order order) => context.Orders.Add(order);

    public void DiscardOrder(Order order)
    {
        foreach (var line in order.OrderLines.ToList()) context.Entry(line).State = EntityState.Detached;
        context.Entry(order).State = EntityState.Detached;
    }

    public Task<bool> OrderExistsAsync(string tcgplayerOrderId, CancellationToken cancellationToken) =>
        context.Orders.AnyAsync(order => order.TcgplayerOrderId == tcgplayerOrderId, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DuplicateKeyDetector.IsDuplicateKeyViolation(exception))
        {
            throw new UniqueConstraintViolationException("The order identifier already exists.", exception);
        }
        catch (DbUpdateException exception)
        {
            throw new OrderPersistenceException("The order could not be persisted atomically.", exception);
        }
    }
}
