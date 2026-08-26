using LootSingles.Application.Orders;
using LootSingles.Application.Persistence;
using LootSingles.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.Infrastructure.Persistence;

public sealed class OrderRepository(LootSinglesDbContext context) : IOrderRepository
{
    /// <summary>
    /// Bound on FIFO-candidate retries for <see cref="ClaimNextAvailableAsync"/> before giving up
    /// (research.md §2) — high enough to absorb ordinary contention without looping unboundedly.
    /// </summary>
    private const int MaxClaimNextAttempts = 5;

    public async Task<Order?> ClaimNextAvailableAsync(
        int actorEmployeeId,
        CancellationToken cancellationToken
    )
    {
        var excludedOrderIds = new List<int>();

        for (var attempt = 0; attempt < MaxClaimNextAttempts; attempt++)
        {
            var candidateId = await context
                .Orders.AsNoTracking()
                .Where(order =>
                    order.Status == OrderStatus.Ready
                    && order.ClaimedByEmployeeId == null
                    && !excludedOrderIds.Contains(order.Id)
                )
                .OrderBy(order => order.ImportedAt)
                .Select(order => (int?)order.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (candidateId is null)
            {
                return null;
            }

            var rowsAffected = await TryClaimAsync(
                candidateId.Value,
                actorEmployeeId,
                cancellationToken
            );

            if (rowsAffected == 1)
            {
                return await context
                    .Orders.AsNoTracking()
                    .Include(order => order.ClaimedByEmployee)
                    .SingleAsync(order => order.Id == candidateId, cancellationToken);
            }

            excludedOrderIds.Add(candidateId.Value);
        }

        return null;
    }

    public async Task<ClaimAttemptResult> ClaimSpecificAsync(
        int orderId,
        int actorEmployeeId,
        CancellationToken cancellationToken
    )
    {
        var rowsAffected = await TryClaimAsync(orderId, actorEmployeeId, cancellationToken);

        var currentOrder = await context
            .Orders.AsNoTracking()
            .Include(order => order.ClaimedByEmployee)
            .SingleOrDefaultAsync(order => order.Id == orderId, cancellationToken);

        return new ClaimAttemptResult(rowsAffected == 1, currentOrder);
    }

    public Task<int?> GetActiveClaimedOrderIdAsync(
        int employeeId,
        CancellationToken cancellationToken
    ) =>
        context
            .Orders.AsNoTracking()
            .Where(order => order.ClaimedByEmployeeId == employeeId)
            .Select(order => (int?)order.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The conditional compare-and-swap claim primitive (research.md §1): only succeeds while the
    /// target order is still unclaimed. Reused by both "Pick Next Order" and "Choose Order."
    /// </summary>
    private async Task<int> TryClaimAsync(
        int orderId,
        int actorEmployeeId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await context
                .Orders.Where(order => order.Id == orderId && order.ClaimedByEmployeeId == null)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(order => order.ClaimedByEmployeeId, actorEmployeeId)
                            .SetProperty(order => order.ClaimedAt, DateTimeOffset.UtcNow)
                            .SetProperty(order => order.Status, OrderStatus.InProgress),
                    cancellationToken
                );
        }
        catch (Exception exception) when (DuplicateKeyDetector.IsDuplicateKeyViolation(exception))
        {
            throw new UniqueConstraintViolationException(
                "Employee already has an active claim on another order.",
                exception
            );
        }
    }

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

    public Task<OrderDetail?> GetByIdAsync(int orderId, CancellationToken cancellationToken)
    {
        return context
            .Orders.AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => new OrderDetail(
                order.Id,
                order.TcgplayerOrderId,
                order.Status,
                order
                    .OrderLines.OrderBy(line => line.Id)
                    .Select(line => new OrderLineDetail(
                        line.ProductName,
                        line.ProductLine,
                        line.Set,
                        line.CollectorNumber,
                        line.Rarity,
                        line.Variant,
                        line.Condition,
                        line.Quantity
                    ))
                    .ToList()
            ))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
