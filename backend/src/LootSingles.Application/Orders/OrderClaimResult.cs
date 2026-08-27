using LootSingles.Domain.Orders;

namespace LootSingles.Application.Orders;

public enum OrderClaimOutcome
{
    Success,
    OrderNotFound,
    AlreadyClaimed,
    NoOrdersAvailable,
    EmployeeHasActiveClaim,
    NotYourClaim,
    OrderNotClaimed,
}

public sealed record OrderClaimResult(
    OrderClaimOutcome Outcome,
    Order? Order = null,
    int? ConflictingOrderId = null
)
{
    public static OrderClaimResult Success(Order order) => new(OrderClaimOutcome.Success, order);

    public static readonly OrderClaimResult OrderNotFound = new(OrderClaimOutcome.OrderNotFound);

    public static OrderClaimResult AlreadyClaimed(Order order) =>
        new(OrderClaimOutcome.AlreadyClaimed, order);

    public static readonly OrderClaimResult NoOrdersAvailable = new(
        OrderClaimOutcome.NoOrdersAvailable
    );

    public static OrderClaimResult EmployeeHasActiveClaim(int? conflictingOrderId) =>
        new(OrderClaimOutcome.EmployeeHasActiveClaim, ConflictingOrderId: conflictingOrderId);

    public static readonly OrderClaimResult NotYourClaim = new(OrderClaimOutcome.NotYourClaim);

    public static readonly OrderClaimResult OrderNotClaimed = new(
        OrderClaimOutcome.OrderNotClaimed
    );
}
