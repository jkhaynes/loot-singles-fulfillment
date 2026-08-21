using LootSingles.Application.Import;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Import;

public class RejectionTests
{
    [Theory]
    [InlineData("missing-order-identifier.pdf", FailureType.MissingOrderIdentifier)]
    [InlineData("zero-product-lines.pdf", FailureType.NoProductLines)]
    [InlineData("invalid-quantity.pdf", FailureType.InvalidQuantity)]
    [InlineData("missing-product-name.pdf", FailureType.MissingProductName)]
    [InlineData("missing-set.pdf", FailureType.MissingSet)]
    [InlineData("missing-collector-number.pdf", FailureType.MissingCollectorNumber)]
    [InlineData("missing-condition.pdf", FailureType.MissingCondition)]
    public async Task ImportAsync_MalformedOrder_RejectsWithoutPersistingOrder(
        string fixture,
        FailureType expectedFailure)
    {
        await using var context = ImportTestSupport.CreateInMemoryContext();
        var final = await ImportTestSupport.ImportFixtureAsync(
            ImportTestSupport.CreateService(context), fixture);

        Assert.Empty(await context.Orders.ToListAsync());
        var result = Assert.Single(final.ImportAttempt.ImportOrderResults);
        Assert.Equal(ImportOutcome.Rejected, result.Outcome);
        Assert.Equal(expectedFailure, result.FailureCode);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureMessage));
        Assert.Null(result.ResultingOrderId);
    }

    [Fact]
    public async Task ImportAsync_CorruptedFile_RecordsUnreadablePdfWithoutOrderResults()
    {
        await using var context = ImportTestSupport.CreateInMemoryContext();
        var final = await ImportTestSupport.ImportFixtureAsync(
            ImportTestSupport.CreateService(context), "corrupted-file.pdf");

        Assert.Equal(FailureType.UnreadablePdf, final.ImportAttempt.AttemptFailureCode);
        Assert.False(string.IsNullOrWhiteSpace(final.ImportAttempt.AttemptFailureMessage));
        Assert.Empty(final.ImportAttempt.ImportOrderResults);
        Assert.Empty(await context.Orders.ToListAsync());
    }

    [Fact]
    public async Task ImportAsync_SummaryMismatch_RejectsAttemptBeforePersistingOrders()
    {
        await using var context = ImportTestSupport.CreateInMemoryContext();
        var final = await ImportTestSupport.ImportFixtureAsync(
            ImportTestSupport.CreateService(context), "summary-mismatch.pdf");

        Assert.Equal(FailureType.SummaryMismatch, final.ImportAttempt.AttemptFailureCode);
        Assert.Contains("SUM-ACTUAL-IDX", final.ImportAttempt.AttemptFailureMessage);
        Assert.Contains("SUM-DIFFERENT-IDX", final.ImportAttempt.AttemptFailureMessage);
        Assert.Empty(await context.Orders.ToListAsync());
    }
}
