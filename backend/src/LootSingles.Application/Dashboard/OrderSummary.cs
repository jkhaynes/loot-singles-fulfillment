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

/// <summary>
/// A Needs Attention row, which also names the flagged product(s) so the problem is visible
/// without opening the order (015-pick-completion FR-014).
/// </summary>
public sealed record NeedsAttentionOrderSummary(
    int OrderId,
    string TcgplayerOrderId,
    int ProductCount,
    int TotalQuantity,
    IReadOnlyList<string> FlaggedProductNames
);
