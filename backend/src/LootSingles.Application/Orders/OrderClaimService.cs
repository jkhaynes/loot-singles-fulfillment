using LootSingles.Application.Persistence;
using Microsoft.Extensions.Logging;

namespace LootSingles.Application.Orders;

/// <summary>
/// Performs exclusive, concurrency-safe order-claiming operations (013-order-claiming).
/// </summary>
public sealed class OrderClaimService(
    IOrderRepository repository,
    ILogger<OrderClaimService> logger
)
{
    public async Task<OrderClaimResult> PickNextAsync(
        int actorEmployeeId,
        CancellationToken cancellationToken
    )
    {
        var activeClaimId = await repository.GetActiveClaimedOrderIdAsync(
            actorEmployeeId,
            cancellationToken
        );
        if (activeClaimId is not null)
        {
            logger.LogInformation(
                "Employee {EmployeeId} requested Pick Next Order but already holds order {OrderId}.",
                actorEmployeeId,
                activeClaimId
            );
            return OrderClaimResult.EmployeeHasActiveClaim(activeClaimId.Value);
        }

        try
        {
            var order = await repository.ClaimNextAvailableAsync(
                actorEmployeeId,
                cancellationToken
            );
            if (order is null)
            {
                logger.LogInformation(
                    "Employee {EmployeeId} requested Pick Next Order but none were available.",
                    actorEmployeeId
                );
                return OrderClaimResult.NoOrdersAvailable;
            }

            logger.LogInformation(
                "Employee {EmployeeId} claimed order {OrderId} via Pick Next Order.",
                actorEmployeeId,
                order.Id
            );
            return OrderClaimResult.Success(order);
        }
        catch (UniqueConstraintViolationException)
        {
            var conflictingOrderId = await repository.GetActiveClaimedOrderIdAsync(
                actorEmployeeId,
                cancellationToken
            );
            logger.LogInformation(
                "Employee {EmployeeId} lost a Pick Next Order race to their own concurrent claim on order {OrderId}.",
                actorEmployeeId,
                conflictingOrderId
            );
            return OrderClaimResult.EmployeeHasActiveClaim(conflictingOrderId ?? 0);
        }
    }
}
