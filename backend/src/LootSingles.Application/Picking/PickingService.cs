using Microsoft.Extensions.Logging;

namespace LootSingles.Application.Picking;

/// <summary>
/// Records per-line picking outcomes for the claim holder (015-pick-completion).
/// </summary>
public sealed class PickingService(IPickingRepository repository, ILogger<PickingService> logger)
{
    public async Task<PickingResult> RecordPickedAsync(
        int orderId,
        int orderLineId,
        int actorEmployeeId,
        CancellationToken cancellationToken
    )
    {
        var result = await repository.RecordOutcomeAsync(
            orderId,
            orderLineId,
            actorEmployeeId,
            new PickOutcomeChange.Picked(),
            cancellationToken
        );

        if (result.Outcome == PickingOutcome.Success)
        {
            logger.LogInformation(
                "Employee {EmployeeId} confirmed line {OrderLineId} of order {OrderId} as picked; order is now {OrderStatus}.",
                actorEmployeeId,
                orderLineId,
                orderId,
                result.OrderStatus
            );
        }
        else
        {
            logger.LogInformation(
                "Employee {EmployeeId} could not confirm line {OrderLineId} of order {OrderId}: {Outcome}.",
                actorEmployeeId,
                orderLineId,
                orderId,
                result.Outcome
            );
        }

        return result;
    }
}
