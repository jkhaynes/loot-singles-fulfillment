namespace LootSingles.Application.Import;

/// <summary>
/// Thrown when persisting an order's data fails. <see cref="IsDuplicate"/> distinguishes a
/// duplicate-key violation on the TCGplayer order identifier from any other persistence
/// failure; per FR-016, both are always represented as a per-order rejection by the caller,
/// never allowed to propagate further, so sibling orders in the same batch still persist —
/// see contracts/import-service.md and research.md §5.
/// </summary>
public sealed class OrderPersistenceException(
    string message,
    bool isDuplicate,
    Exception innerException) : Exception(message, innerException)
{
    public bool IsDuplicate { get; } = isDuplicate;
}
