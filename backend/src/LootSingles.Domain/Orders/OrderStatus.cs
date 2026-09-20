namespace LootSingles.Domain.Orders;

/// <summary>
/// Represents the status of an imported order.
/// Always derived from the order's claim state and its lines' current pick outcomes
/// (015-pick-completion FR-006) — never set as an independent flag.
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// Order is ready for picking.
    /// A successfully imported order begins in this state.
    /// </summary>
    Ready = 0,

    /// <summary>
    /// Order is exclusively claimed by an employee and being worked (013-order-claiming).
    /// </summary>
    InProgress = 1,

    /// <summary>
    /// Every line is confirmed picked and none has an unresolved issue (015-pick-completion FR-007).
    /// </summary>
    Picked = 2,

    /// <summary>
    /// At least one line has an unresolved picking issue (015-pick-completion FR-008).
    /// </summary>
    NeedsAttention = 3,
}
