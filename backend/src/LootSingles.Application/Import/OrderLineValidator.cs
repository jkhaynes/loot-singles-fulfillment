using LootSingles.Domain.Orders;

namespace LootSingles.Application.Import;

public static class OrderLineValidator
{
    public static OrderLineValidationResult Validate(RawProductLine source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!int.TryParse(source.QuantityText, out var quantity) || quantity <= 0)
            return Invalid(FailureType.InvalidQuantity, $"Quantity '{source.QuantityText}' is not a positive whole number for product line '{source.RawDescription}'.");

        OrderLine orderLine;
        try
        {
            orderLine = OrderLineExtractor.Extract(source);
        }
        catch (FormatException exception) when (exception.Message.Contains("collector number", StringComparison.Ordinal))
        {
            return Invalid(FailureType.MissingCollectorNumber, $"Collector number is missing or unreadable for product line '{source.RawDescription}'.");
        }
        catch (FormatException)
        {
            return Invalid(FailureType.MissingProductName, $"Product name is missing or unreadable for product line '{source.RawDescription}'.");
        }

        if (string.IsNullOrWhiteSpace(orderLine.ProductName))
            return Invalid(FailureType.MissingProductName, $"Product name is missing for product line '{source.RawDescription}'.");
        if (string.IsNullOrWhiteSpace(orderLine.Set))
            return Invalid(FailureType.MissingSet, $"Set is missing for product line '{source.RawDescription}'.");
        if (string.IsNullOrWhiteSpace(orderLine.CollectorNumber))
            return Invalid(FailureType.MissingCollectorNumber, $"Collector number is missing for product line '{source.RawDescription}'.");
        if (string.IsNullOrWhiteSpace(orderLine.Condition))
            return Invalid(FailureType.MissingCondition, $"Condition is missing or unrecognized for product line '{source.RawDescription}'.");

        return new OrderLineValidationResult(orderLine, null, null);
    }

    private static OrderLineValidationResult Invalid(FailureType type, string message) => new(null, type, message);
}

public sealed record OrderLineValidationResult(OrderLine? OrderLine, FailureType? FailureType, string? FailureMessage)
{
    public bool IsValid => OrderLine is not null;
}
