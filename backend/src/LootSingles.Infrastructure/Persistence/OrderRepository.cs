using LootSingles.Application.Orders;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.Infrastructure.Persistence;

public sealed class OrderRepository(LootSinglesDbContext context) : IOrderRepository
{
    public async Task<IReadOnlyList<OrderListItem>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await context
            .Orders.AsNoTracking()
            .OrderByDescending(order => order.ImportedAt)
            .ThenBy(order => order.TcgplayerOrderId)
            .Select(order => new OrderListItem(
                order.Id,
                order.TcgplayerOrderId,
                order.Status,
                order.ImportedAt
            ))
            .ToListAsync(cancellationToken);
    }
}
