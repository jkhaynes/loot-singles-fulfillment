namespace LootSingles.Application.Dashboard;

/// <summary>
/// A Dashboard-only read-model row (data-model.md) — not persisted, not a
/// <c>LootSingles.Domain</c> entity. Represents one order in the Ready section.
/// </summary>
public sealed record OrderSummary(
    int OrderId,
    string TcgplayerOrderId,
    int ProductCount,
    int TotalQuantity
);
