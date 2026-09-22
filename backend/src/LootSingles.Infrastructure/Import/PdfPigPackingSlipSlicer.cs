using LootSingles.Application.Import;
using UglyToad.PdfPig.Writer;

namespace LootSingles.Infrastructure.Import;

/// <summary>
/// Slices one order's slip out of a batch document using PdfPig's <see cref="PdfMerger"/>
/// (017-pick-completion-handoff, research.md §2).
/// <para>
/// No new dependency: PdfPig was already here to read packing slips. Measured against the real
/// fixtures, a thirteen-order 35KB batch yields a ~3KB single-order document that reopens cleanly
/// with its text intact.
/// </para>
/// </summary>
public sealed class PdfPigPackingSlipSlicer : IPackingSlipSlicer
{
    public byte[]? Slice(byte[] document, IReadOnlyList<int> pageNumbers)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pageNumbers);

        if (pageNumbers.Count == 0)
        {
            return null;
        }

        try
        {
            var slice = PdfMerger.Merge([document], [pageNumbers]);

            // A merge that produced nothing is a failure wearing a success's clothes; treat it as
            // the absence it is rather than storing an empty slip.
            return slice.Length == 0 ? null : slice;
        }
        catch (Exception)
        {
            // Deliberately broad. A slip is a convenience for the packing bench, and no failure
            // to produce one may reject an order whose own data parsed successfully (FR-021).
            // The caller records that it happened; see PackingSlipImportService.
            return null;
        }
    }
}
