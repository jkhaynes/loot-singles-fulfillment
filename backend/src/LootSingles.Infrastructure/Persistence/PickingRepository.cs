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
            PickOutcomeChange.IssueReport => PickOutcome.HasIssue,
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, null),
        };
        var recordedAt = DateTimeOffset.UtcNow;

        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken
        );

        // PackedAt is part of the lock rather than a check before it. A packed order is
        // terminal (FR-031), and recording an outcome on one is how an order ended up Packed
        // while a line was HasIssue: the pack was legitimate, and an issue was reported
        // afterwards by a picker who still held the claim. Because Packed short-circuits the
        // status derivation, nothing downstream corrected it.
        var claimLocked = await context
            .Orders.Where(order =>
                order.Id == orderId
                && order.ClaimedByEmployeeId == actorEmployeeId
                && order.PackedAt == null
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

        int? currentPickingIssueId = null;
        if (change is PickOutcomeChange.IssueReport report)
        {
            // Load-bearing, not duplication of the rows-affected check below (branch review
            // BR-009): PickingIssue.OrderLineId is a foreign key, so inserting against a line id
            // that does not exist raises a constraint violation — a 500 — before the later check
            // can reject it as LineNotFound. A line belonging to a *different* order passes this
            // guard and is caught below, which is why removing this looked safe.
            if (
                !await context.OrderLines.AnyAsync(
                    line => line.Id == orderLineId && line.OrderId == orderId,
                    cancellationToken
                )
            )
            {
                await transaction.RollbackAsync(cancellationToken);
                return PickingResult.LineNotFound;
            }

            var issue = new PickingIssue
            {
                OrderLineId = orderLineId,
                IssueType = report.IssueType,
                RequiredQuantity = report.RequiredQuantity,
                FoundQuantity = report.FoundQuantity,
                Note = report.Note,
                ReportedByEmployeeId = actorEmployeeId,
                ReportedAt = recordedAt,
            };
            context.PickingIssues.Add(issue);
            await context.SaveChangesAsync(cancellationToken);
            context.Entry(issue).State = EntityState.Detached;
            currentPickingIssueId = issue.Id;
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
                        .SetProperty(
                            line => line.PickOutcomeRecordedAt,
                            (DateTimeOffset?)recordedAt
                        )
                        .SetProperty(line => line.CurrentPickingIssueId, currentPickingIssueId),
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

        // Read the full detail inside the transaction (research.md §2, branch review BR-002): the
        // row lock is still held, so this is exactly the state this call produced — never a later
        // interleaved write, and never a second round trip through image enrichment.
        var updatedOrder = await context
            .Orders.AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(OrderDetailProjection.ToDetail)
            .SingleAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return PickingResult.Success(updatedOrder);
    }

    private async Task<PickingResult> ClassifyRejectionAsync(
        int orderId,
        int orderLineId,
        CancellationToken cancellationToken
    )
    {
        var order = await context
            .Orders.AsNoTracking()
            .Where(candidate => candidate.Id == orderId)
            .Select(candidate => new { candidate.PackedAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return PickingResult.OrderNotFound;
        }

        if (order.PackedAt is not null)
        {
            // Checked before the claim, because the employee may genuinely hold it and being
            // told otherwise would send them looking for a problem that is not there.
            return PickingResult.OrderAlreadyPacked;
        }

        var lineInOrder = await context.OrderLines.AnyAsync(
            line => line.Id == orderLineId && line.OrderId == orderId,
            cancellationToken
        );

        return lineInOrder ? PickingResult.NotYourClaim : PickingResult.LineNotFound;
    }
}
