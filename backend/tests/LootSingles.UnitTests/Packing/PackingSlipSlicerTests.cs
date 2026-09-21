using LootSingles.Application.Import;
using LootSingles.Infrastructure.Import;
using UglyToad.PdfPig;

namespace LootSingles.UnitTests.Packing;

/// <summary>
/// 017-pick-completion-handoff T031/T032 — slicing one order's slip out of a batch.
/// </summary>
public sealed class PackingSlipSlicerTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "LootSingles.Fixtures",
        "PackingSlips"
    );

    private readonly IPackingSlipSlicer slicer = new PdfPigPackingSlipSlicer();

    [Fact]
    public void Slice_SingleOrderPage_ProducesAOneOrderDocument()
    {
        var batch = Fixture("valid-multi-order-batch.pdf");

        var slip = slicer.Slice(batch, [1]);

        Assert.NotNull(slip);
        using var document = PdfDocument.Open(slip);
        Assert.Equal(1, document.NumberOfPages);
        // One customer's order, and demonstrably not the batch's other twelve (FR-019).
        Assert.Contains("F0000001", document.GetPage(1).Text);
        Assert.DoesNotContain("F0000002", document.GetPage(1).Text);
    }

    [Fact]
    public void Slice_MultiPageOrder_KeepsEveryPageAndNoOthers()
    {
        var batch = Fixture("multi-page-order-no-total-on-continuation-pages.pdf");

        var slip = slicer.Slice(batch, [1, 2]);

        Assert.NotNull(slip);
        using var document = PdfDocument.Open(slip);
        Assert.Equal(2, document.NumberOfPages);
    }

    [Fact]
    public void Slice_IsSmallerThanTheBatchItCameFrom()
    {
        // The point of slicing rather than storing the batch: one file, one customer, and a
        // fraction of the bytes.
        var batch = Fixture("valid-multi-order-batch.pdf");

        var slip = slicer.Slice(batch, [1]);

        Assert.NotNull(slip);
        Assert.True(
            slip.Length < batch.Length,
            $"expected the slice ({slip.Length} bytes) to be smaller than the batch ({batch.Length} bytes)"
        );
    }

    [Fact]
    public void Slice_PageOutOfRange_ReturnsNullRatherThanThrowing()
    {
        // FR-021. A slip that cannot be produced must never reject the order it belongs to, so
        // this reports failure as a value the import pipeline can absorb.
        var batch = Fixture("valid-multi-order-batch.pdf");

        var slip = slicer.Slice(batch, [9999]);

        Assert.Null(slip);
    }

    [Fact]
    public void Slice_UnreadableDocument_ReturnsNull()
    {
        var slip = slicer.Slice(Fixture("corrupted-file.pdf"), [1]);

        Assert.Null(slip);
    }

    [Fact]
    public void Slice_NoPages_ReturnsNull()
    {
        var slip = slicer.Slice(Fixture("valid-multi-order-batch.pdf"), []);

        Assert.Null(slip);
    }

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(FixtureDirectory, name));
}
