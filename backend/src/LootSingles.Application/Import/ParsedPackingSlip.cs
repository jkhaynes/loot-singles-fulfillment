namespace LootSingles.Application.Import;

/// <summary>
/// The raw result of parsing a packing slip PDF via <see cref="IPackingSlipParser"/>,
/// before any field-level validation.
/// </summary>
public class ParsedPackingSlip
{
    /// <summary>
    /// One raw block per order found in the file, in the order each order's first page appears.
    /// A multi-page order's blocks are already merged (see <see cref="RawOrderBlock"/>).
    /// </summary>
    public required IReadOnlyList<RawOrderBlock> OrderBlocks { get; set; }

    /// <summary>
    /// Whether a trailing summary/index page was found in the file (research.md §2).
    /// </summary>
    public required bool SummaryPageFound { get; set; }

    /// <summary>
    /// The order identifiers listed on the summary/index page, for the FR-013 cross-check
    /// against <see cref="OrderBlocks"/>. Empty when <see cref="SummaryPageFound"/> is false.
    /// </summary>
    public required IReadOnlyList<string> SummaryOrderIdentifiers { get; set; }
}
