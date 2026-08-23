using LootSingles.Application.Dashboard;
using LootSingles.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.Infrastructure.Persistence;

/// <summary>
/// <see cref="IDashboardRepository"/> over <see cref="LootSinglesDbContext"/>. Computes
/// <see cref="OrderSummary.ProductCount"/>/<see cref="OrderSummary.TotalQuantity"/> server-side
/// within the query projection (data-model.md's Query shape, constitution's EF Core standards).
/// </summary>
public sealed class DashboardRepository(LootSinglesDbContext context) : IDashboardRepository
{
    public async Task<IReadOnlyList<OrderSummary>> GetReadyOrderSummariesAsync(
        CancellationToken cancellationToken
    )
    {
        return await context
            .Orders.AsNoTracking()
            .Where(order => order.Status == OrderStatus.Ready)
            .OrderBy(order => order.ImportedAt)
            .ThenBy(order => order.TcgplayerOrderId)
            .Select(order => new OrderSummary(
                order.Id,
                order.TcgplayerOrderId,
                order.OrderLines.Count,
                order.OrderLines.Sum(line => line.Quantity)
            ))
            .ToListAsync(cancellationToken);
    }
}
