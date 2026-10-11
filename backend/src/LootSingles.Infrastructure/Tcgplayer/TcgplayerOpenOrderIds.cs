namespace LootSingles.Infrastructure.Tcgplayer;

/// <summary>
/// The ids each import resolves from the order manifest (call #3) for the configured open order
/// statuses, open pickup statuses and order types. The order search sends all three together as
/// its filters, and its result is the list of open orders (FR-004, research.md §3).
/// </summary>
public sealed record TcgplayerOpenOrderIds(
    IReadOnlyList<int> OrderStatusIds,
    IReadOnlyList<int> PickupStatusIds,
    IReadOnlyList<int> OrderTypeIds
);
