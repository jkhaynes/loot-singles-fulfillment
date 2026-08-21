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
    public async Task<IReadOnlyList<OrderSummary>> GetReadyOrderSummariesAsync(CancellationToken cancellationToken)
    {
        var readyOrders = await context.Orders
            .AsNoTracking()
            .Where(order => order.Status == OrderStatus.Ready)
            .Select(order => new
            {
                order.ImportedAt,
                Summary = new OrderSummary(
                    order.Id,
                    order.TcgplayerOrderId,
                    order.OrderLines.Count,
                    order.OrderLines.Sum(line => line.Quantity)),
            })
            .ToListAsync(cancellationToken);

        // SQLite cannot translate DateTimeOffset ordering; this bounded Ready-order set is
        // ordered after materialization so production SQL Server and integration SQLite agree
        // (same pattern as EmployeeRepository.GetAuditEventsAsync).
        return readyOrders
            .OrderBy(row => row.ImportedAt)
            .Select(row => row.Summary)
            .ToList();
    }
}
