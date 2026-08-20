using LootSingles.Infrastructure.Import;

namespace LootSingles.UnitTests.Import;

public class PdfPigPackingSlipParserTests
{
    [Fact]
    public void Parse_SanitizedMultiOrderBatch_ReturnsThirteenOrdersAndSummaryIndex()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "PackingSlips",
            "valid-multi-order-batch.pdf");
        using var pdf = File.OpenRead(fixturePath);
        var parser = new PdfPigPackingSlipParser();

        var result = parser.Parse(pdf);

        Assert.Equal(13, result.OrderBlocks.Count);
        Assert.All(result.OrderBlocks, block =>
        {
            Assert.False(string.IsNullOrWhiteSpace(block.OrderIdentifier));
            Assert.NotEmpty(block.ProductLines);
        });
        Assert.True(result.SummaryPageFound);
        Assert.Equal(13, result.SummaryOrderIdentifiers.Count);
        Assert.Equal(
            result.OrderBlocks.Select(block => block.OrderIdentifier).Order(),
            result.SummaryOrderIdentifiers.Order());
    }

    [Fact]
    public void Parse_DuplicateProductLineFixture_PreservesBothRowsSeparately()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "PackingSlips",
            "duplicate-product-line-same-order.pdf");
        using var pdf = File.OpenRead(fixturePath);
        var parser = new PdfPigPackingSlipParser();

        var result = parser.Parse(pdf);

        var order = Assert.Single(result.OrderBlocks);
        Assert.Equal("DUPLICATE-LINE-FIXTURE", order.OrderIdentifier);
        Assert.Equal(2, order.ProductLines.Count);
        Assert.Equal(new[] { "1", "2" }, order.ProductLines.Select(line => line.QuantityText));
        Assert.Equal(order.ProductLines[0].RawDescription, order.ProductLines[1].RawDescription);
        Assert.Equal(new[] { "DUPLICATE-LINE-FIXTURE" }, result.SummaryOrderIdentifiers);
    }
}
