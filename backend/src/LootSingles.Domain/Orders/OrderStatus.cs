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

    /// <summary>
    /// The order's sleeve has been packed and dispatched. Terminal (PRD §20.1,
    /// 017-pick-completion-handoff FR-031) — nothing returns an order from here.
    /// <para>
    /// <b>The one status that is not derived.</b> Every other value is recomputed from the order's
    /// current lines and claim state on each write, because each of them is a fact about those
    /// lines. Packing is not: nothing about an order's lines changes when its sleeve goes in the
    /// mail. It is an event, recorded as <see cref="Order.PackedAt"/> and
    /// <see cref="Order.PackedByEmployeeId"/>, and it short-circuits the derivation rather than
    /// joining it (FR-035, research.md §6).
    /// </para>
    /// </summary>
    Packed = 4,
}
