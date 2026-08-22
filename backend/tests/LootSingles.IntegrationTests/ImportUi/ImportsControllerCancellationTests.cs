using System.Runtime.CompilerServices;
using System.Text.Json;
using LootSingles.Application.Import;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LootSingles.IntegrationTests.ImportUi;

[CollectionDefinition("Import HTTP cancellation", DisableParallelization = true)]
public sealed class ImportCancellationCollection
{
    public const string Name = "Import HTTP cancellation";
}

[Collection(ImportCancellationCollection.Name)]
public sealed class ImportsControllerCancellationTests
{
    [Fact]
    public async Task AbortedRequestPreservesCommittedOrderAndRetryImportsRemainingOrderOnce()
    {
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPackingSlipImportService>();
                services.AddSingleton<CancellationScenarioState>();
                services.AddScoped<IPackingSlipImportService, CancellationScenarioService>();
            })
        );
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        var state = factory.Services.GetRequiredService<CancellationScenarioState>();
        using var cancellation = new CancellationTokenSource();
        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/imports");
        firstRequest.Content = ImportUiTestSupport.FileForm([1]);

        using var firstResponse = await client.SendAsync(
            firstRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellation.Token
        );
        await using var firstStream = await firstResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(firstStream);

        var firstLine = await reader.ReadLineAsync();
        Assert.NotNull(firstLine);
        using (var firstSnapshot = JsonDocument.Parse(firstLine))
        {
            Assert.Equal(1, firstSnapshot.RootElement.GetProperty("ordersProcessed").GetInt32());
        }

        cancellation.Cancel();
        reader.Dispose();
        await firstStream.DisposeAsync();
        firstResponse.Dispose();
        await state.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
            var orders = await context.Orders.Include(order => order.OrderLines).ToListAsync();

            Assert.Single(orders);
            Assert.Equal("CANCEL-COMMITTED", orders[0].TcgplayerOrderId);
            Assert.Single(orders[0].OrderLines);
            Assert.DoesNotContain(orders, order => order.TcgplayerOrderId == "CANCEL-IN-FLIGHT");
        }

        using var retryForm = ImportUiTestSupport.FileForm([1]);
        var retryResponse = await client.PostAsync("/api/imports", retryForm);
        var retryLines = (await retryResponse.Content.ReadAsStringAsync()).Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries
        );
        using var terminal = JsonDocument.Parse(retryLines[^1]);
        var results = terminal.RootElement.GetProperty("results").EnumerateArray().ToArray();

        Assert.Contains(
            results,
            result =>
                result.GetProperty("sourceOrderIdentifier").GetString() == "CANCEL-COMMITTED"
                && result.GetProperty("failureCode").GetString() == "duplicateOrder"
        );
        Assert.Contains(
            results,
            result =>
                result.GetProperty("sourceOrderIdentifier").GetString() == "CANCEL-REMAINING"
                && result.GetProperty("outcome").GetString() == "succeeded"
        );

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
            var orders = await context.Orders.Include(order => order.OrderLines).ToListAsync();

            Assert.Equal(2, orders.Count);
            Assert.All(orders, order => Assert.Single(order.OrderLines));
            Assert.All(
                orders.GroupBy(order => order.TcgplayerOrderId),
                group => Assert.Single(group)
            );
        }
    }

    private sealed class CancellationScenarioState
    {
        private int _invocationCount;

        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int NextInvocation() => Interlocked.Increment(ref _invocationCount);
    }

    private sealed class CancellationScenarioService(
        LootSinglesDbContext context,
        CancellationScenarioState state
    ) : IPackingSlipImportService
    {
        public async IAsyncEnumerable<ImportProgressUpdate> ImportAsync(
            Stream packingSlipPdf,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            if (state.NextInvocation() > 1)
            {
                var retryService = new PackingSlipImportService(
                    new RetryParser(),
                    new ImportRepository(context)
                );
                await foreach (
                    var update in retryService
                        .ImportAsync(packingSlipPdf, cancellationToken)
                        .WithCancellation(cancellationToken)
                )
                {
                    yield return update;
                }

                yield break;
            }

            var committedOrder = NewOrder("CANCEL-COMMITTED");
            context.Orders.Add(committedOrder);
            await context.SaveChangesAsync(cancellationToken);

            var attempt = new ImportAttempt { StartedAt = DateTimeOffset.UtcNow };
            attempt.ImportOrderResults.Add(
                new ImportOrderResult
                {
                    ImportAttemptId = 0,
                    SourceOrderIdentifier = committedOrder.TcgplayerOrderId,
                    Outcome = ImportOutcome.Succeeded,
                    ResultingOrderId = committedOrder.Id,
                }
            );
            yield return new ImportProgressUpdate(2, 1, 1, 0, false, attempt);

            context.Orders.Add(NewOrder("CANCEL-IN-FLIGHT"));
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    state.CancellationObserved.TrySetResult();
                }
            }
        }

        private static Order NewOrder(string identifier) =>
            new()
            {
                TcgplayerOrderId = identifier,
                Status = OrderStatus.Ready,
                ImportedAt = DateTimeOffset.UtcNow,
                OrderLines =
                [
                    new OrderLine
                    {
                        RawDescription = "Pokemon - Test Set: Test Card - #1 - Common - Near Mint",
                        ProductLine = "Pokemon",
                        ProductName = "Test Card",
                        Set = "Test Set",
                        CollectorNumber = "#1",
                        Rarity = "Common",
                        Condition = "Near Mint",
                        Quantity = 1,
                    },
                ],
            };
    }

    private sealed class RetryParser : IPackingSlipParser
    {
        public async IAsyncEnumerable<PackingSlipParseUpdate> ParseAsync(
            Stream packingSlipPdf,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.Yield();
            yield return new PackingSlipParseUpdate(
                2,
                true,
                new ParsedPackingSlip
                {
                    OrderBlocks = [NewBlock("CANCEL-COMMITTED"), NewBlock("CANCEL-REMAINING")],
                    SummaryPageFound = false,
                    SummaryOrderIdentifiers = [],
                }
            );
        }

        private static RawOrderBlock NewBlock(string identifier) =>
            new()
            {
                OrderIdentifier = identifier,
                ProductLines =
                [
                    new RawProductLine
                    {
                        QuantityText = "1",
                        RawDescription = "Pokemon - Test Set: Test Card - #1 - Common - Near Mint",
                    },
                ],
            };
    }
}
