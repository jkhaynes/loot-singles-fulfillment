using LootSingles.Application.Import;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Import;

public class PartialImportTests
{
    [Fact]
    public async Task ImportAsync_OneBadOrder_PersistsValidSiblingsAndRejectsOnlyBadOrder()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext();
        var final = await ImportTestSupport.ImportFixtureAsync(
            ImportTestSupport.CreateService(context),
            "partial-batch-one-bad-order.pdf"
        );

        Assert.Equal(2, await context.Orders.CountAsync());
        Assert.Equal(2, final.SucceededCount);
        Assert.Equal(1, final.FailedCount);
        Assert.Equal(3, final.OrdersProcessed);
        Assert.Contains(
            final.ImportAttempt.ImportOrderResults,
            result =>
                result.Outcome == ImportOutcome.Rejected
                && result.FailureCode == FailureType.MissingOrderIdentifier
        );
    }
}
