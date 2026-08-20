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

}
