using LootSingles.Application.Picking;
using LootSingles.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.Infrastructure.Persistence;

public sealed class PickingRepository(LootSinglesDbContext context) : IPickingRepository
{
    /// <summary>
    /// research.md §2: one explicit transaction whose first statement is a claim-gated no-op
    /// UPDATE on the order row. That both authorizes the actor (0 rows → not their claim) and
    /// takes the order's exclusive lock before any line is touched — the same order→lines lock
    /// sequence release/force-release use when they re-derive status, so a concurrent release
    /// waits for this commit (or wins first and makes this call reject) instead of deadlocking.
    /// </summary>
    public async Task<PickingResult> RecordOutcomeAsync(
        int orderId,
        int orderLineId,
        int actorEmployeeId,
        PickOutcomeChange change,
        CancellationToken cancellationToken
    )
    {
        var pickOutcome = change switch
        {
            PickOutcomeChange.Picked => PickOutcome.Picked,
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, null),
        };
        var recordedAt = DateTimeOffset.UtcNow;

        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken
        );

        var claimLocked = await context
            .Orders.Where(order =>
                order.Id == orderId && order.ClaimedByEmployeeId == actorEmployeeId
            )
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(order => order.Status, order => order.Status),
                cancellationToken
            );

        if (claimLocked != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ClassifyRejectionAsync(orderId, orderLineId, cancellationToken);
        }

        var linesUpdated = await context
            .OrderLines.Where(line => line.Id == orderLineId && line.OrderId == orderId)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(line => line.PickOutcome, (PickOutcome?)pickOutcome)
                        .SetProperty(
                            line => line.PickOutcomeRecordedByEmployeeId,
                            (int?)actorEmployeeId
                        )
                        .SetProperty(line => line.PickOutcomeRecordedAt, (DateTimeOffset?)recordedAt)
                        .SetProperty(line => line.CurrentPickingIssueId, (int?)null),
                cancellationToken
            );

        if (linesUpdated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PickingResult.LineNotFound;
        }

        await context
            .Orders.Where(order => order.Id == orderId)
            .ExecuteUpdateAsync(
                setters =>
                    setters.SetProperty(
                        order => order.Status,
                        OrderStatusComputation.FromCurrentLines(claimedAfterWrite: true)
                    ),
                cancellationToken
            );

        var orderStatus = await context
            .Orders.AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => order.Status)
            .SingleAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return PickingResult.Success(orderStatus);
    }

    private async Task<PickingResult> ClassifyRejectionAsync(
        int orderId,
        int orderLineId,
        CancellationToken cancellationToken
    )
    {
        if (!await context.Orders.AnyAsync(order => order.Id == orderId, cancellationToken))
        {
            return PickingResult.OrderNotFound;
        }

        var lineInOrder = await context.OrderLines.AnyAsync(
            line => line.Id == orderLineId && line.OrderId == orderId,
            cancellationToken
        );

        return lineInOrder ? PickingResult.NotYourClaim : PickingResult.LineNotFound;
    }
}
