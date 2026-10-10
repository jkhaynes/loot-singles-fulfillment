namespace LootSingles.Application.Import;

/// <summary>
/// The validation shared by every import source. Returns null when the candidate may become a
/// domain order, otherwise the first failure. Messages match the PDF importer's originals.
/// A missing collector number is valid here; the PDF-only requirement lives in the extractor.
/// </summary>
public static class OrderCandidateValidator
{
    public static (FailureType Type, string Message)? Validate(OrderCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.RejectedBySource is { } rejection)
            return rejection;

        if (string.IsNullOrWhiteSpace(candidate.SourceOrderIdentifier))
            return (
                FailureType.MissingOrderIdentifier,
                "An order page is missing its order identifier."
            );
        var orderId = candidate.SourceOrderIdentifier;
        if (candidate.Lines.Count == 0)
            return (FailureType.NoProductLines, $"Order '{orderId}' contains no product lines.");

        foreach (var line in candidate.Lines)
        {
            if (ValidateLine(line) is { } failure)
                return (failure.Type, $"Order '{orderId}': {failure.Message}");
        }
        return null;
    }

    private static (FailureType Type, string Message)? ValidateLine(OrderLineCandidate line)
    {
        if (line.Quantity is not > 0)
            return (
                FailureType.InvalidQuantity,
                $"Quantity '{line.Quantity}' is not a positive whole number for product line '{line.RawDescription}'."
            );
        if (string.IsNullOrWhiteSpace(line.ProductName))
            return (
                FailureType.MissingProductName,
                $"Product name is missing for product line '{line.RawDescription}'."
            );
        if (string.IsNullOrWhiteSpace(line.Set))
            return (
                FailureType.MissingSet,
                $"Set is missing for product line '{line.RawDescription}'."
            );
        if (string.IsNullOrWhiteSpace(line.Condition))
            return (
                FailureType.MissingCondition,
                $"Condition is missing or unrecognized for product line '{line.RawDescription}'."
            );
        return null;
    }
}
