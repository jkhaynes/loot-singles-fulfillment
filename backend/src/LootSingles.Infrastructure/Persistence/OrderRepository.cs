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

    public Task<OrderDetail?> GetByIdAsync(
        int orderId,
        CancellationToken cancellationToken
    )
    {
        return context
            .Orders.AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => new OrderDetail(
                order.Id,
                order.TcgplayerOrderId,
                order
                    .OrderLines.OrderBy(line => line.Id)
                    .Select(line => new OrderLineDetail(
                        line.ProductName,
                        line.Set,
                        line.Variant,
                        line.Condition,
                        line.Quantity
                    ))
                    .ToList()
            ))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
