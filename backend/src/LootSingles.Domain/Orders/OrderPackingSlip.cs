namespace LootSingles.Domain.Orders;

/// <summary>
/// One order's packing slip, sliced out of the imported batch document at import time
/// (017-pick-completion-handoff FR-019) so the packing desk can print it.
/// <para>
/// <b>This is the only place in the application that holds customer personal information.</b> It
/// carries the customer's name and shipping address, because that is what a packing slip is. PRD
/// v0.5 §27 permits it for the packing workflow and bounds it with four rules that hold together
/// or not at all: one order per file, no picking surface able to reach a slip, every retrieval
/// recorded (<see cref="PackingSlipAccess"/>), and a retention rule deferred and tracked as PRD
/// §41 open question 59.
/// </para>
/// <para>
/// It lives in its own table rather than as a column on <see cref="Order"/> so that loading an
/// order never materialises slip bytes. That keeps "no picking surface reaches a slip" a
/// structural property instead of a rule every present and future projection has to remember
/// (research.md §4).
/// </para>
/// </summary>
public class OrderPackingSlip
{
    /// <summary>
    /// The order this slip belongs to, and the primary key — one slip per order at most.
    /// </summary>
    public int OrderId { get; set; }

    /// <summary>
    /// The slip document's bytes: exactly this order's pages from the batch document, and never
    /// another order's. The batch document itself is not retained (FR-020).
    /// </summary>
    public required byte[] Content { get; set; }

    /// <summary>
    /// When the slip was sliced out and stored.
    /// </summary>
    public DateTimeOffset StoredAt { get; set; }

    /// <summary>
    /// Navigation to the owning order.
    /// </summary>
    public Order? Order { get; set; }
}
