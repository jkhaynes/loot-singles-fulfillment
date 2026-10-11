using Microsoft.Extensions.Logging;

namespace LootSingles.Application.Orders;

/// <summary>
/// Manager/Admin order deletion (021-local-mock-orders). The role check is the controller's; the
/// production-versus-stage rule is applied by the repository from <c>allowTouched</c>.
/// </summary>
public sealed class OrderDeletionService(
    IOrderDeletionRepository repository,
    OrderDeletionOptions options,
    TimeProvider timeProvider,
    ILogger<OrderDeletionService> logger
)
{
    public async Task<OrderDeletionResult> DeleteAsync(
        int orderId,
        int managerId,
        CancellationToken cancellationToken
    )
    {
        var attempt = await repository.DeleteAsync(
            orderId,
            managerId,
            timeProvider.GetUtcNow(),
            options.AllowTouchedOrders,
            cancellationToken
        );

        switch (attempt.Outcome)
        {
            case OrderDeletionOutcome.Deleted:
                logger.LogInformation(
                    "Manager {ManagerId} deleted order {OrderId} ({TcgplayerOrderId}).",
                    managerId,
                    orderId,
                    attempt.TcgplayerOrderId
                );
                return OrderDeletionResult.Deleted;
            case OrderDeletionOutcome.NotFound:
                return OrderDeletionResult.NotFound;
            case OrderDeletionOutcome.AlreadyDeleted:
                return OrderDeletionResult.AlreadyDeleted;
            default:
                return OrderDeletionResult.ClaimedBefore;
        }
    }

    public async Task<OrderDeleteAllResult> DeleteAllAsync(
        int managerId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken
    )
    {
        var now = timeProvider.GetUtcNow();
        if (asOf > now)
        {
            return OrderDeleteAllResult.InvalidAsOf;
        }

        var counts = await repository.DeleteAllAsync(
            managerId,
            now,
            asOf,
            options.AllowTouchedOrders,
            cancellationToken
        );

        logger.LogInformation(
            "Manager {ManagerId} deleted {DeletedCount} orders; {KeptCount} kept.",
            managerId,
            counts.DeletedCount,
            counts.KeptCount
        );
        return OrderDeleteAllResult.Done(counts.DeletedCount, counts.KeptCount);
    }

    public async Task<OrderDeletionSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var counts = await repository.GetSummaryAsync(
            now,
            options.AllowTouchedOrders,
            cancellationToken
        );
        return new OrderDeletionSummary(
            now,
            counts.DeletableCount,
            counts.KeptCount,
            counts.ClaimedCount,
            counts.WithUnresolvedIssueCount
        );
    }
}
