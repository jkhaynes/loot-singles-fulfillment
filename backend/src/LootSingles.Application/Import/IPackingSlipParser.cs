namespace LootSingles.Application.Import;

/// <summary>
/// Extracts raw, unvalidated per-order data from a TCGplayer packing slip PDF.
/// This is the replaceable integration seam (Constitution IX) between this feature's
/// parse/validate/persist pipeline and the underlying PDF library — a future TCGplayer
/// API integration can implement this interface without touching orchestration code.
/// <para>
/// Implementations must not persist the supplied stream or any copy of it; the caller owns the
/// stream's lifecycle. Reading is all this seam does — producing a document is a separate job
/// with its own seam (<c>IPackingSlipSlicer</c>), so that replacing this parser with a structured
/// integration does not drag slicing out with it.
/// </para>
/// <para>
/// This previously cited 001-tcgplayer-order-import FR-019, which forbade retaining any artifact
/// carrying customer shipping details. PRD v0.5 §27 (amendment A14, approved 2026-09-21) reversed
/// that in part: the application now stores one packing slip <em>per order</em> for the packing
/// workflow (017-pick-completion-handoff). The whole batch document is still never retained, which
/// is why the instruction above stands on its own terms rather than on FR-019's.
/// </para>
/// </summary>
public interface IPackingSlipParser
{
    /// <summary>
    /// Incrementally parses a packing slip PDF, reporting the number of order pages
    /// discovered so far and returning the complete raw result in the final update.
    /// Performs no field-level validation — that is the caller's responsibility once this
    /// raw data is extracted.
    /// </summary>
    /// <param name="packingSlipPdf">A readable stream of the packing slip PDF's bytes.</param>
    /// <param name="cancellationToken">Stops parsing without converting cancellation into a rejection.</param>
    IAsyncEnumerable<PackingSlipParseUpdate> ParseAsync(
        Stream packingSlipPdf,
        CancellationToken cancellationToken = default
    );
}
