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
    public Task<IReadOnlyList<OrderSummary>> GetReadyOrderSummariesAsync(
        CancellationToken cancellationToken
    ) => SummariesWithStatusAsync(OrderStatus.Ready, cancellationToken);

    public Task<IReadOnlyList<OrderSummary>> GetInProgressOrderSummariesAsync(
        CancellationToken cancellationToken
    ) => SummariesWithStatusAsync(OrderStatus.InProgress, cancellationToken);

    public Task<IReadOnlyList<OrderSummary>> GetPickedOrderSummariesAsync(
        CancellationToken cancellationToken
    ) => SummariesWithStatusAsync(OrderStatus.Picked, cancellationToken);

    public async Task<
        IReadOnlyList<NeedsAttentionOrderSummary>
    > GetNeedsAttentionOrderSummariesAsync(CancellationToken cancellationToken) =>
        await OrderedOrdersWithStatus(OrderStatus.NeedsAttention)
            .Select(order => new NeedsAttentionOrderSummary(
                order.Id,
                order.TcgplayerOrderId,
                order.OrderLines.Count,
                order.OrderLines.Sum(line => line.Quantity),
                order
                    .OrderLines.Where(line => line.PickOutcome == PickOutcome.HasIssue)
                    .OrderBy(line => line.Id)
                    .Select(line => line.ProductName)
                    .ToList()
            ))
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<OrderSummary>> SummariesWithStatusAsync(
        OrderStatus status,
        CancellationToken cancellationToken
    ) =>
        await OrderedOrdersWithStatus(status)
            .Select(order => new OrderSummary(
                order.Id,
                order.TcgplayerOrderId,
                order.OrderLines.Count,
                order.OrderLines.Sum(line => line.Quantity)
            ))
            .ToListAsync(cancellationToken);

    private IQueryable<Order> OrderedOrdersWithStatus(OrderStatus status) =>
        context
            .Orders.AsNoTracking()
            .Where(order => order.Status == status)
            .OrderBy(order => order.ImportedAt)
            .ThenBy(order => order.TcgplayerOrderId);
}
