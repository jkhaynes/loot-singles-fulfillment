using LootSingles.Domain.Orders;

namespace LootSingles.Application.Import;

public sealed record OrderLineValidationResult(OrderLine? OrderLine, FailureType? FailureType, string? FailureMessage)
{
    public bool IsValid => OrderLine is not null;

    public static OrderLineValidationResult Invalid(FailureType type, string message) => new(null, type, message);
}
