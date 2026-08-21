namespace LootSingles.Application.Import;

/// <summary>
/// Thrown when persisting an order's data fails for a reason other than a duplicate order
/// identifier — see <see cref="LootSingles.Application.Persistence.UniqueConstraintViolationException"/>
/// for that case. Per FR-016, both are always represented as a per-order rejection by the
/// caller, never allowed to propagate further, so sibling orders in the same batch still persist
/// — see contracts/import-service.md and research.md §5.
/// </summary>
public sealed class OrderPersistenceException(string message, Exception innerException)
    : Exception(message, innerException);
