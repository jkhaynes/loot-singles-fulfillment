using LootSingles.Application.Orders;
using LootSingles.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.Infrastructure.Persistence;

/// <summary>
/// Order soft delete (021 research R7). Each call is one transaction: a conditional
/// <c>ExecuteUpdateAsync</c> that sets <c>DeletedAt</c> and <c>DeletedByEmployeeId</c> together,
/// then removal of the packing slips of the orders it just marked. The update is the same
/// compare-and-swap as a claim (<see cref="OrderRepository"/>), so a claim and a delete of one
/// order serialize on the row lock and exactly one of them succeeds.
/// </summary>
/// <remarks>
/// The <c>DeletedAt == null</c> part of every condition comes from the <c>Order</c> query filter.
/// <c>IgnoreQueryFilters()</c> is used only to word a refusal and to find the rows this call
/// marked (data-model.md).
/// </remarks>
public sealed class OrderDeletionRepository(LootSinglesDbContext context) : IOrderDeletionRepository
{
    public async Task<DeleteOrderAttempt> DeleteAsync(
        int orderId,
        int actorEmployeeId,
        DateTimeOffset now,
        bool allowTouched,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken
        );

        var rowsAffected = await context.Orders.Deletable(allowTouched)
            .Where(order => order.Id == orderId)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(order => order.DeletedAt, now)
                        .SetProperty(order => order.DeletedByEmployeeId, actorEmployeeId),
                cancellationToken
            );

        DeleteOrderAttempt attempt;
        if (rowsAffected == 1)
        {
            await context
                .OrderPackingSlips.Where(slip => slip.OrderId == orderId)
                .ExecuteDeleteAsync(cancellationToken);
            var tcgplayerOrderId = await context
                .Orders.IgnoreQueryFilters()
                .Where(order => order.Id == orderId)
                .Select(order => order.TcgplayerOrderId)
                .SingleAsync(cancellationToken);
            attempt = new DeleteOrderAttempt(OrderDeletionOutcome.Deleted, tcgplayerOrderId);
        }
        else
        {
            var current = await context
                .Orders.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(order => order.Id == orderId)
                .Select(order => new { order.DeletedAt })
                .SingleOrDefaultAsync(cancellationToken);
            var outcome =
                current is null ? OrderDeletionOutcome.NotFound
                : current.DeletedAt is not null ? OrderDeletionOutcome.AlreadyDeleted
                : OrderDeletionOutcome.ClaimedBefore;
            attempt = new DeleteOrderAttempt(outcome, null);
        }

        await transaction.CommitAsync(cancellationToken);
        return attempt;
    }

    public async Task<DeleteAllCounts> DeleteAllAsync(
        int actorEmployeeId,
        DateTimeOffset now,
        DateTimeOffset asOf,
        bool allowTouched,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken
        );

        var deletedCount = await context.Orders.Deletable(allowTouched)
            .Where(order => order.ImportedAt <= asOf)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(order => order.DeletedAt, now)
                        .SetProperty(order => order.DeletedByEmployeeId, actorEmployeeId),
                cancellationToken
            );

        // Exactly the orders the update above marked (research R7).
        await context
            .OrderPackingSlips.Where(slip =>
                context
                    .Orders.IgnoreQueryFilters()
                    .Any(order =>
                        order.Id == slip.OrderId
                        && order.DeletedAt == now
                        && order.DeletedByEmployeeId == actorEmployeeId
                    )
            )
            .ExecuteDeleteAsync(cancellationToken);

        // Visible orders in scope that the update left: the ones the rule keeps.
        var keptCount = await context.Orders.CountAsync(
            order => order.ImportedAt <= asOf,
            cancellationToken
        );

        await transaction.CommitAsync(cancellationToken);
        return new DeleteAllCounts(deletedCount, keptCount);
    }

    public async Task<OrderDeletionCounts> GetSummaryAsync(
        DateTimeOffset now,
        bool allowTouched,
        CancellationToken cancellationToken
    )
    {
        var visible = context.Orders.AsNoTracking().Where(order => order.ImportedAt <= now);
        var deletable = context.Orders.Deletable(allowTouched)
            .AsNoTracking()
            .Where(order => order.ImportedAt <= now);

        var visibleCount = await visible.CountAsync(cancellationToken);
        var deletableCount = await deletable.CountAsync(cancellationToken);
        var claimedCount = await deletable.CountAsync(
            order => order.ClaimedByEmployeeId != null,
            cancellationToken
        );
        var withUnresolvedIssueCount = await deletable.CountAsync(
            order => order.OrderLines.Any(line => line.PickOutcome == PickOutcome.HasIssue),
            cancellationToken
        );

        return new OrderDeletionCounts(
            deletableCount,
            visibleCount - deletableCount,
            claimedCount,
            withUnresolvedIssueCount
        );
    }
}
