using LootSingles.Domain.Orders;

namespace LootSingles.Application.Picking;

public enum PickingOutcome
{
    Success,
    OrderNotFound,
    LineNotFound,
    NotYourClaim,
}

/// <param name="OrderStatus">The order's status as derived by the recording write; set on success.</param>
public sealed record PickingResult(PickingOutcome Outcome, OrderStatus? OrderStatus = null)
{
    public static PickingResult Success(OrderStatus orderStatus) =>
        new(PickingOutcome.Success, orderStatus);

    public static readonly PickingResult OrderNotFound = new(PickingOutcome.OrderNotFound);

    public static readonly PickingResult LineNotFound = new(PickingOutcome.LineNotFound);

    public static readonly PickingResult NotYourClaim = new(PickingOutcome.NotYourClaim);
}
