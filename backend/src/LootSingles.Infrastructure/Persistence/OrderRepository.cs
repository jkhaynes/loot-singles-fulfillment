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

            var attemptResult = await ExecuteConditionalUpdateAsync(
                candidateId.Value,
                ct =>
                    context
                        .Orders.Where(order =>
                            order.Id == candidateId && order.ClaimedByEmployeeId == null
                        )
                        .ExecuteUpdateAsync(
                            setters =>
                                setters
                                    .SetProperty(
                                        order => order.ClaimedByEmployeeId,
                                        actorEmployeeId
                                    )
                                    .SetProperty(order => order.ClaimedAt, DateTimeOffset.UtcNow)
                                    .SetProperty(order => order.Status, OrderStatus.InProgress),
                            ct
                        ),
                cancellationToken
            );

            if (attemptResult.Succeeded)
            {
                return attemptResult.Order;
            }

            excludedOrderIds.Add(candidateId.Value);
        }

        return null;
    }

    public Task<ClaimAttemptResult> ClaimSpecificAsync(
        int orderId,
        int actorEmployeeId,
        CancellationToken cancellationToken
    ) =>
        ExecuteConditionalUpdateAsync(
            orderId,
            ct =>
                context
                    .Orders.Where(order => order.Id == orderId && order.ClaimedByEmployeeId == null)
                    .ExecuteUpdateAsync(
                        setters =>
                            setters
                                .SetProperty(order => order.ClaimedByEmployeeId, actorEmployeeId)
                                .SetProperty(order => order.ClaimedAt, DateTimeOffset.UtcNow)
                                .SetProperty(order => order.Status, OrderStatus.InProgress),
                        ct
                    ),
            cancellationToken
        );

    public Task<ClaimAttemptResult> ReleaseAsync(
        int orderId,
        int actorEmployeeId,
        CancellationToken cancellationToken
    ) =>
        ExecuteConditionalUpdateAsync(
            orderId,
            ct =>
                context
                    .Orders.Where(order =>
                        order.Id == orderId && order.ClaimedByEmployeeId == actorEmployeeId
                    )
                    .ExecuteUpdateAsync(
                        setters =>
                            setters
                                .SetProperty(order => order.ClaimedByEmployeeId, (int?)null)
                                .SetProperty(order => order.ClaimedAt, (DateTimeOffset?)null)
                                .SetProperty(order => order.Status, OrderStatus.Ready),
                        ct
                    ),
            cancellationToken
        );

    public Task<ClaimAttemptResult> ForceReleaseAsync(
        int orderId,
        CancellationToken cancellationToken
    ) =>
        ExecuteConditionalUpdateAsync(
            orderId,
            ct =>
                context
                    .Orders.Where(order => order.Id == orderId && order.ClaimedByEmployeeId != null)
                    .ExecuteUpdateAsync(
                        setters =>
                            setters
                                .SetProperty(order => order.ClaimedByEmployeeId, (int?)null)
                                .SetProperty(order => order.ClaimedAt, (DateTimeOffset?)null)
                                .SetProperty(order => order.Status, OrderStatus.Ready),
                        ct
                    ),
            cancellationToken
        );

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
    /// The shared conditional compare-and-swap primitive (research.md §1) behind every claim,
    /// release, and force-release write. Runs the caller's conditional <c>ExecuteUpdateAsync</c> and
    /// a follow-up "what does the order look like now" read inside one explicit transaction, so the
    /// row's exclusive lock (held from the UPDATE until COMMIT) prevents any other request from
    /// mutating it in between — the returned <see cref="ClaimAttemptResult.Order"/> is guaranteed to
    /// reflect exactly the state this call itself produced (or found), never a later interleaved
    /// write from someone else (code-design-review M1).
    /// </summary>
    private async Task<ClaimAttemptResult> ExecuteConditionalUpdateAsync(
        int orderId,
        Func<CancellationToken, Task<int>> executeUpdate,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken
        );

        int rowsAffected;
        try
        {
            rowsAffected = await executeUpdate(cancellationToken);
        }
        catch (Exception exception) when (DuplicateKeyDetector.IsDuplicateKeyViolation(exception))
        {
            throw new UniqueConstraintViolationException(
                "Employee already has an active claim on another order.",
                exception
            );
        }

        var currentOrder = await context
            .Orders.AsNoTracking()
            .Include(order => order.ClaimedByEmployee)
            .SingleOrDefaultAsync(order => order.Id == orderId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new ClaimAttemptResult(rowsAffected == 1, currentOrder);
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
                order.ImportedAt,
                order.ClaimedByEmployeeId,
                order.ClaimedByEmployee != null ? order.ClaimedByEmployee.DisplayName : null
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
                    .ToList(),
                order.ClaimedByEmployeeId,
                order.ClaimedByEmployee != null ? order.ClaimedByEmployee.DisplayName : null
            ))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
