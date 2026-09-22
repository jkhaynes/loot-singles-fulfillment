using LootSingles.Domain.Employees;

namespace LootSingles.Domain.Orders;

/// <summary>
/// Represents one TCGplayer order recovered from a packing slip import.
/// An Order contains one or more OrderLines and does not contain any customer shipping information.
/// </summary>
public class Order
{
    /// <summary>
    /// Primary key, auto-incremented identity.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The TCGplayer order identifier from the packing slip (e.g., "F8433182-7B9B3B-BC75E").
    /// This is the natural business identifier and must be unique across all orders.
    /// </summary>
    public required string TcgplayerOrderId { get; set; }

    /// <summary>
    /// The current status of this order.
    /// A successfully imported Order begins in the "Ready" state.
    /// </summary>
    public OrderStatus Status { get; set; }

    /// <summary>
    /// The timestamp when this order was imported.
    /// </summary>
    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>
    /// The collection of order lines (product/card items) contained in this order.
    /// An Order must contain at least one OrderLine.
    /// </summary>
    public ICollection<OrderLine> OrderLines { get; set; } = new List<OrderLine>();

    /// <summary>
    /// The employee currently claiming this order, if any (013-order-claiming). A claimed order is
    /// never <see cref="OrderStatus.Ready"/>; an unclaimed one is never
    /// <see cref="OrderStatus.InProgress"/>. Picked and NeedsAttention orders may be either
    /// (015-pick-completion).
    /// </summary>
    public int? ClaimedByEmployeeId { get; set; }

    /// <summary>
    /// When the current claim was made. Null iff <see cref="ClaimedByEmployeeId"/> is null.
    /// Informational only — claims never expire automatically (FR-008).
    /// </summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    /// <summary>
    /// Navigation to the claiming employee, for surfacing the claimant's display name (FR-005).
    /// </summary>
    public Employee? ClaimedByEmployee { get; set; }

    /// <summary>
    /// When this order's sleeve was recorded as packed (017-pick-completion-handoff FR-032).
    /// Null until packed, and never cleared afterwards — <see cref="OrderStatus.Packed"/> is
    /// terminal (FR-031).
    /// <para>
    /// This is the one input to <see cref="Status"/> that is recorded rather than derived. When it
    /// is set, the status derivation short-circuits to <see cref="OrderStatus.Packed"/>; when it is
    /// null, status is derived from the lines exactly as feature 015 established.
    /// </para>
    /// </summary>
    public DateTimeOffset? PackedAt { get; set; }

    /// <summary>
    /// Which employee recorded the pack. Null iff <see cref="PackedAt"/> is null — the two are
    /// always written together, because either alone answers half of PRD §34's "who packed it, and
    /// when".
    /// </summary>
    public int? PackedByEmployeeId { get; set; }

    /// <summary>
    /// Navigation to the packing employee, for surfacing their display name at the packing desk.
    /// </summary>
    public Employee? PackedByEmployee { get; set; }

    /// <summary>
    /// This order's stored packing slip, when one was sliced out of the batch at import time
    /// (017-pick-completion-handoff FR-019). Null for orders imported before that feature and
    /// for any order whose slip could not be produced (FR-022) — absence is a normal state.
    /// <para>
    /// A reference navigation, so it is never materialised unless something explicitly asks for
    /// it. No picking query does, which is what keeps customer data off those paths (FR-039).
    /// </para>
    /// </summary>
    public OrderPackingSlip? PackingSlip { get; set; }
}
