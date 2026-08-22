namespace LootSingles.Application.Import;

public static class OrderLineValidator
{
    public static OrderLineValidationResult Validate(RawProductLine source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!int.TryParse(source.QuantityText, out var quantity) || quantity <= 0)
            return OrderLineValidationResult.Invalid(
                FailureType.InvalidQuantity,
                $"Quantity '{source.QuantityText}' is not a positive whole number for product line '{source.RawDescription}'."
            );

        var extraction = OrderLineExtractor.Extract(source);
        if (!extraction.IsValid)
            return extraction;

        var orderLine = extraction.OrderLine!;
        if (string.IsNullOrWhiteSpace(orderLine.ProductName))
            return OrderLineValidationResult.Invalid(
                FailureType.MissingProductName,
                $"Product name is missing for product line '{source.RawDescription}'."
            );
        if (string.IsNullOrWhiteSpace(orderLine.Set))
            return OrderLineValidationResult.Invalid(
                FailureType.MissingSet,
                $"Set is missing for product line '{source.RawDescription}'."
            );
        if (string.IsNullOrWhiteSpace(orderLine.CollectorNumber))
            return OrderLineValidationResult.Invalid(
                FailureType.MissingCollectorNumber,
                $"Collector number is missing for product line '{source.RawDescription}'."
            );
        if (string.IsNullOrWhiteSpace(orderLine.Condition))
            return OrderLineValidationResult.Invalid(
                FailureType.MissingCondition,
                $"Condition is missing or unrecognized for product line '{source.RawDescription}'."
            );

        return new OrderLineValidationResult(orderLine, null, null);
    }
}
