using LootSingles.Application.Orders;

namespace LootSingles.Application.Picking;

public enum PickingOutcome
{
    Success,
    OrderNotFound,
    LineNotFound,
    NotYourClaim,
    InvalidIssueType,
    InvalidIssueDetails,

    /// <summary>
    /// The order has been packed, so its lines are no longer workable
    /// (017-pick-completion-handoff FR-031). Distinct from <see cref="NotYourClaim"/> because
    /// the employee may well hold the claim — the order has simply left.
    /// </summary>
    OrderAlreadyPacked,
}

/// <param name="Order">
/// On success, the order as the recording transaction itself committed it (branch review BR-002) —
/// never a later re-read, so the response, the log line, and the persisted row are one observation.
/// </param>
public sealed record PickingResult(PickingOutcome Outcome, OrderDetail? Order = null)
{
    public static PickingResult Success(OrderDetail order) => new(PickingOutcome.Success, order);

    public static readonly PickingResult OrderNotFound = new(PickingOutcome.OrderNotFound);

    public static readonly PickingResult LineNotFound = new(PickingOutcome.LineNotFound);

    public static readonly PickingResult OrderAlreadyPacked = new(
        PickingOutcome.OrderAlreadyPacked
    );

    public static readonly PickingResult NotYourClaim = new(PickingOutcome.NotYourClaim);

    public static readonly PickingResult InvalidIssueType = new(PickingOutcome.InvalidIssueType);

    /// <summary>
    /// The issue type is recognized, but the note or quantities it carries are not acceptable.
    /// </summary>
    public static readonly PickingResult InvalidIssueDetails = new(
        PickingOutcome.InvalidIssueDetails
    );
}
