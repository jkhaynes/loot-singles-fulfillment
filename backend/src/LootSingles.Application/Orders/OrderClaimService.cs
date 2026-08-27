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
            return OrderClaimResult.EmployeeHasActiveClaim(conflictingOrderId);
        }
    }

    public async Task<OrderClaimResult> ClaimAsync(
        int orderId,
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
                "Employee {EmployeeId} attempted to choose order {OrderId} but already holds order {ActiveClaimId}.",
                actorEmployeeId,
                orderId,
                activeClaimId
            );
            return OrderClaimResult.EmployeeHasActiveClaim(activeClaimId.Value);
        }

        try
        {
            var attempt = await repository.ClaimSpecificAsync(
                orderId,
                actorEmployeeId,
                cancellationToken
            );

            if (attempt.Succeeded)
            {
                logger.LogInformation(
                    "Employee {EmployeeId} claimed order {OrderId} via Choose Order.",
                    actorEmployeeId,
                    orderId
                );
                return OrderClaimResult.Success(attempt.Order!);
            }

            if (attempt.Order is null)
            {
                return OrderClaimResult.OrderNotFound;
            }

            logger.LogInformation(
                "Employee {EmployeeId} attempted to choose order {OrderId} but it is already claimed by employee {ClaimantId}.",
                actorEmployeeId,
                orderId,
                attempt.Order.ClaimedByEmployeeId
            );
            return OrderClaimResult.AlreadyClaimed(attempt.Order);
        }
        catch (UniqueConstraintViolationException)
        {
            var conflictingOrderId = await repository.GetActiveClaimedOrderIdAsync(
                actorEmployeeId,
                cancellationToken
            );
            logger.LogInformation(
                "Employee {EmployeeId} lost a Choose Order race to their own concurrent claim on order {OrderId}.",
                actorEmployeeId,
                conflictingOrderId
            );
            return OrderClaimResult.EmployeeHasActiveClaim(conflictingOrderId);
        }
    }

    public async Task<OrderClaimResult> ReleaseAsync(
        int orderId,
        int actorEmployeeId,
        CancellationToken cancellationToken
    )
    {
        var attempt = await repository.ReleaseAsync(orderId, actorEmployeeId, cancellationToken);

        if (attempt.Succeeded)
        {
            logger.LogInformation(
                "Employee {EmployeeId} released order {OrderId}.",
                actorEmployeeId,
                orderId
            );
            return OrderClaimResult.Success(attempt.Order!);
        }

        if (attempt.Order is null)
        {
            return OrderClaimResult.OrderNotFound;
        }

        logger.LogInformation(
            "Employee {EmployeeId} attempted to release order {OrderId} but does not hold its claim.",
            actorEmployeeId,
            orderId
        );
        return OrderClaimResult.NotYourClaim;
    }

    public async Task<OrderClaimResult> ForceReleaseAsync(
        int orderId,
        int actorEmployeeId,
        CancellationToken cancellationToken
    )
    {
        var attempt = await repository.ForceReleaseAsync(orderId, cancellationToken);

        if (attempt.Succeeded)
        {
            logger.LogInformation(
                "Manager {ManagerId} force-released order {OrderId}.",
                actorEmployeeId,
                orderId
            );
            return OrderClaimResult.Success(attempt.Order!);
        }

        if (attempt.Order is null)
        {
            return OrderClaimResult.OrderNotFound;
        }

        logger.LogInformation(
            "Manager {ManagerId} attempted to force-release order {OrderId} but it is not currently claimed.",
            actorEmployeeId,
            orderId
        );
        return OrderClaimResult.OrderNotClaimed;
    }
}
