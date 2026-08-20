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
    /// Parses a packing slip PDF into one raw block per order page, plus the detected
    /// summary/index page's order-identifier list (if present) for the FR-013 cross-check.
    /// Performs no field-level validation — that is the caller's responsibility once this
    /// raw data is extracted.
    /// </summary>
    /// <param name="packingSlipPdf">A readable stream of the packing slip PDF's bytes.</param>
    ParsedPackingSlip Parse(Stream packingSlipPdf);
}
