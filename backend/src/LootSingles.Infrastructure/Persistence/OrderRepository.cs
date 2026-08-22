using LootSingles.Application.Orders;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.Infrastructure.Persistence;

public sealed class OrderRepository(LootSinglesDbContext context) : IOrderRepository
{
    public async Task<IReadOnlyList<OrderListItem>> GetAllAsync(CancellationToken cancellationToken)
    {
        var orders = await context
            .Orders.AsNoTracking()
            .Select(order => new OrderListItem(
                order.Id,
                order.TcgplayerOrderId,
                order.Status,
                order.ImportedAt
            ))
            .ToListAsync(cancellationToken);

        // SQLite cannot translate DateTimeOffset ordering. Sort after projecting and
        // materializing so integration SQLite and production SQL Server behave identically.
        return orders
            .OrderByDescending(order => order.ImportedAt)
            .ThenBy(order => order.TcgplayerOrderId, StringComparer.Ordinal)
            .ToList();
    }
}
