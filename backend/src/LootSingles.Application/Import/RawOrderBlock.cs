namespace LootSingles.Application.Import;

/// <summary>
/// The raw, unvalidated data extracted from one order's page in a packing slip PDF
/// (research.md §2 — one page per order, anchored on the "Order Number:" line).
/// </summary>
public class RawOrderBlock
{
    /// <summary>
    /// The order identifier as read from the page's "Order Number:" line.
    /// Null when the identifier could not be located or read (the
    /// <see cref="FailureType.MissingOrderIdentifier"/> case) — validation of this
    /// happens downstream, not in the parser.
    /// </summary>
    public required string? OrderIdentifier { get; set; }

    /// <summary>
    /// One entry per row of the page's line-item table, in table order.
    /// </summary>
    public required IReadOnlyList<RawProductLine> ProductLines { get; set; }
}
