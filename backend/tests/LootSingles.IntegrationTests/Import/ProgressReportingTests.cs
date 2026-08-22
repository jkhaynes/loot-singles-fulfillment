using LootSingles.Application.Import;
using LootSingles.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace LootSingles.IntegrationTests.Import;

public class ProgressReportingTests
{
    [Fact]
    public async Task ImportAsync_ReportsMonotonicProgressAndACompleteFinalUpdate()
    {
        await using var context = ImportTestSupport.CreateInMemoryContext();
        var service = ImportTestSupport.CreateService(context);
        await using var fixture = ImportTestSupport.OpenFixture("valid-multi-order-batch.pdf");
        var updates = new List<ImportProgressUpdate>();

        await foreach (var update in service.ImportAsync(fixture))
        {
            updates.Add(update);
        }

        Assert.NotEmpty(updates);
        AssertMonotonic(updates.Select(update => update.OrdersDetected));
        AssertMonotonic(updates.Select(update => update.OrdersProcessed));
        AssertMonotonic(updates.Select(update => update.SucceededCount));
        AssertMonotonic(updates.Select(update => update.FailedCount));
        Assert.All(updates.SkipLast(1), update => Assert.False(update.IsComplete));

        var final = updates[^1];
        Assert.True(final.IsComplete);
        Assert.Equal(final.OrdersDetected, final.OrdersProcessed);
        Assert.Equal(final.OrdersProcessed, final.SucceededCount + final.FailedCount);
        Assert.Equal(13, final.SucceededCount);
        Assert.Equal(0, final.FailedCount);
    }

    [Fact]
    public async Task ImportAsync_TwoHundredOrderBatch_YieldsDiscoveryBeforeParsingCompletes()
    {
        const int orderCount = 200;
        var parser = new SyntheticProgressiveParser(orderCount);
        await using var context = ImportTestSupport.CreateInMemoryContext();
        var service = new PackingSlipImportService(
            parser,
            new ImportRepository(context),
            NullLogger<PackingSlipImportService>.Instance
        );
        await using var source = new MemoryStream([0]);
        await using var updates = service.ImportAsync(source).GetAsyncEnumerator();

        Assert.True(await updates.MoveNextAsync());
        Assert.Equal(1, updates.Current.OrdersDetected);
        Assert.Equal(0, updates.Current.OrdersProcessed);
        Assert.False(updates.Current.IsComplete);
        Assert.False(parser.IsComplete);

        var observed = new List<ImportProgressUpdate> { updates.Current };
        while (await updates.MoveNextAsync())
        {
            observed.Add(updates.Current);
        }

        Assert.True(parser.IsComplete);
        AssertMonotonic(observed.Select(update => update.OrdersDetected));
        AssertMonotonic(observed.Select(update => update.OrdersProcessed));
        Assert.Contains(
            observed,
            update => update.OrdersDetected < orderCount && !update.IsComplete
        );
        var final = observed[^1];
        Assert.True(final.IsComplete);
        Assert.Equal(orderCount, final.OrdersDetected);
        Assert.Equal(orderCount, final.OrdersProcessed);
        Assert.Equal(orderCount, final.SucceededCount);
    }

    private static void AssertMonotonic(IEnumerable<int> values)
    {
        var sequence = values.ToList();
        Assert.All(
            sequence.Zip(sequence.Skip(1)),
            pair =>
                Assert.True(
                    pair.First <= pair.Second,
                    $"Expected a non-decreasing sequence, but found {pair.First} before {pair.Second}."
                )
        );
    }

    private sealed class SyntheticProgressiveParser(int orderCount) : IPackingSlipParser
    {
        public bool IsComplete { get; private set; }

        public async IAsyncEnumerable<PackingSlipParseUpdate> ParseAsync(
            Stream packingSlipPdf,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default
        )
        {
            var blocks = new List<RawOrderBlock>();
            for (var index = 1; index <= orderCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                blocks.Add(
                    new RawOrderBlock
                    {
                        OrderIdentifier = $"SYNTHETIC-{index:D6}-ORDER",
                        ProductLines =
                        [
                            new RawProductLine
                            {
                                QuantityText = "1",
                                RawDescription =
                                    "Pokemon - Test Set: Test Card - #1 - Common - Near Mint",
                            },
                        ],
                    }
                );
                await Task.Yield();
                yield return new PackingSlipParseUpdate(index, false, null);
            }

            IsComplete = true;
            yield return new PackingSlipParseUpdate(
                orderCount,
                true,
                new ParsedPackingSlip
                {
                    OrderBlocks = blocks,
                    SummaryPageFound = false,
                    SummaryOrderIdentifiers = [],
                }
            );
        }
    }
}
