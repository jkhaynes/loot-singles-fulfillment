namespace LootSingles.Application.Import;

/// <summary>
/// One raw row from an order page's line-item table (Quantity | Description | Price | Total Price,
/// research.md §2), before field-level validation. Price/Total Price columns are not extracted
/// (spec Assumptions — not needed for picking).
/// </summary>
public class RawProductLine
{
    /// <summary>
    /// The quantity column's text, exactly as read. Kept as text rather than a parsed number
    /// because it may be blank, zero, negative, or non-numeric — validation of this happens
    /// downstream, not in the parser (FR-006 <see cref="FailureType.InvalidQuantity"/>).
    /// </summary>
    public required string QuantityText { get; set; }

    /// <summary>
    /// The complete raw product description text exactly as it appeared in the packing slip,
    /// unmodified (FR-017). Carried through verbatim to <c>OrderLine.RawDescription</c>.
    /// </summary>
    public required string RawDescription { get; set; }
}
