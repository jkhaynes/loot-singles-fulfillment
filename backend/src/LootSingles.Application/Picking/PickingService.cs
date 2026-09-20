using LootSingles.Domain.Orders;
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
                result.Order?.Status
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

    public async Task<PickingResult> ReportIssueAsync(
        int orderId,
        int orderLineId,
        int actorEmployeeId,
        PickingIssueType issueType,
        int? requiredQuantity,
        int? foundQuantity,
        string? note,
        CancellationToken cancellationToken
    )
    {
        if (!Enum.IsDefined(issueType))
        {
            logger.LogInformation(
                "Employee {EmployeeId} reported an unrecognized issue type on line {OrderLineId} of order {OrderId}.",
                actorEmployeeId,
                orderLineId,
                orderId
            );
            return PickingResult.InvalidIssueType;
        }

        if (
            note is not null && note.Length > PickingIssue.NoteMaxLength
            || requiredQuantity < 0
            || foundQuantity < 0
        )
        {
            logger.LogInformation(
                "Employee {EmployeeId} reported an issue on line {OrderLineId} of order {OrderId} with an unacceptable note length or quantity.",
                actorEmployeeId,
                orderLineId,
                orderId
            );
            return PickingResult.InvalidIssueDetails;
        }

        var result = await repository.RecordOutcomeAsync(
            orderId,
            orderLineId,
            actorEmployeeId,
            new PickOutcomeChange.IssueReport(issueType, requiredQuantity, foundQuantity, note),
            cancellationToken
        );

        if (result.Outcome == PickingOutcome.Success)
        {
            logger.LogInformation(
                "Employee {EmployeeId} reported a {IssueType} issue on line {OrderLineId} of order {OrderId}; order is now {OrderStatus}.",
                actorEmployeeId,
                issueType,
                orderLineId,
                orderId,
                result.Order?.Status
            );
        }
        else
        {
            logger.LogInformation(
                "Employee {EmployeeId} could not report an issue on line {OrderLineId} of order {OrderId}: {Outcome}.",
                actorEmployeeId,
                orderLineId,
                orderId,
                result.Outcome
            );
        }

        return result;
    }
}
