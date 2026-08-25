namespace LootSingles.Application.Import;

/// <summary>
/// The raw, unvalidated data for one order in a packing slip PDF, anchored on its
/// "Order Number:" line. An order may span more than one page (its grand-total row prints only
/// on the final page); the parser merges same-identifier page blocks into one
/// <see cref="RawOrderBlock"/> before returning, so one instance always means one order, never
/// one page.
/// </summary>
public class RawOrderBlock
{
    /// <summary>
    /// The order identifier as read from the "Order Number:" line. Null when the identifier
    /// could not be located or read on the page that produced this block (the
    /// <see cref="FailureType.MissingOrderIdentifier"/> case) — validation of this happens
    /// downstream, not in the parser. A block with a null identifier is never merged with any
    /// other block, including another block with a null identifier.
    /// </summary>
    public required string? OrderIdentifier { get; set; }

    /// <summary>
    /// One entry per row of the order's line-item table, in table order — across every page the
    /// order spans, when it spans more than one.
    /// </summary>
    public required IReadOnlyList<RawProductLine> ProductLines { get; set; }
}
