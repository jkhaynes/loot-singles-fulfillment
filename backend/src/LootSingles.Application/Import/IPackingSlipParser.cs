namespace LootSingles.Application.Import;

/// <summary>
/// Extracts raw, unvalidated per-order data from a TCGplayer packing slip PDF.
/// This is the replaceable integration seam (Constitution IX) between this feature's
/// parse/validate/persist pipeline and the underlying PDF library — a future TCGplayer
/// API integration can implement this interface without touching orchestration code.
/// Implementations must not persist the supplied stream or any copy of it anywhere
/// durable (FR-019); the caller owns the stream's lifecycle.
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
        CancellationToken cancellationToken = default);
}
