namespace LootSingles.Application.Import;

public sealed class OrderLineExtractionException(
    FailureType failureType,
    string message) : FormatException(message)
{
    public FailureType FailureType { get; } = failureType;
}
