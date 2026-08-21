using LootSingles.Infrastructure.Import;
using LootSingles.Application.Import;

namespace LootSingles.UnitTests.Import;

public class PdfPigPackingSlipParserTests
{
    [Fact]
    public void ParserContract_ExposesIncrementalAsyncUpdates()
    {
        var method = typeof(IPackingSlipParser).GetMethod("ParseAsync");

        Assert.NotNull(method);
        Assert.True(method.ReturnType.IsGenericType);
        Assert.Equal(typeof(IAsyncEnumerable<>), method.ReturnType.GetGenericTypeDefinition());
    }

    [Fact]
    public async Task Parse_SanitizedMultiOrderBatch_ReturnsThirteenOrdersAndSummaryIndex()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "PackingSlips",
            "valid-multi-order-batch.pdf");
        using var pdf = File.OpenRead(fixturePath);
        var parser = new PdfPigPackingSlipParser();

        var result = await ParseToCompletionAsync(parser, pdf);

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
    public async Task Parse_DuplicateProductLineFixture_PreservesBothRowsSeparately()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "PackingSlips",
            "duplicate-product-line-same-order.pdf");
        using var pdf = File.OpenRead(fixturePath);
        var parser = new PdfPigPackingSlipParser();

        var result = await ParseToCompletionAsync(parser, pdf);

        var order = Assert.Single(result.OrderBlocks);
        Assert.Equal("DUPLICATE-LINE-FIXTURE", order.OrderIdentifier);
        Assert.Equal(2, order.ProductLines.Count);
        Assert.Equal(new[] { "1", "2" }, order.ProductLines.Select(line => line.QuantityText));
        Assert.Equal(order.ProductLines[0].RawDescription, order.ProductLines[1].RawDescription);
        Assert.Equal(new[] { "DUPLICATE-LINE-FIXTURE" }, result.SummaryOrderIdentifiers);
    }

    private static async Task<ParsedPackingSlip> ParseToCompletionAsync(
        IPackingSlipParser parser,
        Stream source)
    {
        ParsedPackingSlip? result = null;
        await foreach (var update in parser.ParseAsync(source))
        {
            if (update.IsComplete)
            {
                result = update.PackingSlip;
            }
        }

        return Assert.IsType<ParsedPackingSlip>(result);
    }
}
