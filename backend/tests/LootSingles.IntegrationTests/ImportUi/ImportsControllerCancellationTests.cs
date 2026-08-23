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
    public async Task AbortedRequestPreservesCommittedOrderAndRetryImportsRemainingOrdersOnce()
    {
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPackingSlipParser>();
                services.RemoveAll<IImportPersistence>();
                services.AddSingleton<CancellationScenarioState>();
                services.AddScoped<IPackingSlipParser, CancellationScenarioParser>();
                services.AddScoped<ImportRepository>();
                services.AddScoped<IImportPersistence, PausingImportPersistence>();
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
            Assert.Equal(
                "CANCEL-COMMITTED",
                firstSnapshot
                    .RootElement.GetProperty("results")[0]
                    .GetProperty("sourceOrderIdentifier")
                    .GetString()
            );
        }

        await state.SecondOrderSaveReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        reader.Dispose();
        await firstStream.DisposeAsync();
        firstResponse.Dispose();
        await state.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
            var orders = await context.Orders.Include(order => order.OrderLines).ToListAsync();

            var committed = Assert.Single(orders);
            Assert.Equal("CANCEL-COMMITTED", committed.TcgplayerOrderId);
            Assert.Single(committed.OrderLines);
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
                result.GetProperty("sourceOrderIdentifier").GetString() == "CANCEL-IN-FLIGHT"
                && result.GetProperty("outcome").GetString() == "succeeded"
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

            Assert.Equal(3, orders.Count);
            Assert.All(orders, order => Assert.Single(order.OrderLines));
            Assert.All(
                orders.GroupBy(order => order.TcgplayerOrderId),
                group => Assert.Single(group)
            );
        }
    }

    private sealed class CancellationScenarioState
    {
        private int _saveCount;
        private int _pauseClaimed;

        public TaskCompletionSource SecondOrderSaveReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ShouldPauseBeforeSave() =>
            Interlocked.Increment(ref _saveCount) == 4
            && Interlocked.Exchange(ref _pauseClaimed, 1) == 0;
    }

    private sealed class PausingImportPersistence(
        ImportRepository inner,
        CancellationScenarioState state
    ) : IImportPersistence
    {
        public void AddImportAttempt(ImportAttempt attempt) => inner.AddImportAttempt(attempt);

        public void AddOrder(Order order) => inner.AddOrder(order);

        public void DiscardOrder(Order order) => inner.DiscardOrder(order);

        public Task<bool> OrderExistsAsync(
            string tcgplayerOrderId,
            CancellationToken cancellationToken
        ) => inner.OrderExistsAsync(tcgplayerOrderId, cancellationToken);

        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (state.ShouldPauseBeforeSave())
            {
                state.SecondOrderSaveReached.TrySetResult();
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

            await inner.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class CancellationScenarioParser : IPackingSlipParser
    {
        public async IAsyncEnumerable<PackingSlipParseUpdate> ParseAsync(
            Stream packingSlipPdf,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.Yield();
            yield return new PackingSlipParseUpdate(
                3,
                true,
                new ParsedPackingSlip
                {
                    OrderBlocks =
                    [
                        NewBlock("CANCEL-COMMITTED"),
                        NewBlock("CANCEL-IN-FLIGHT"),
                        NewBlock("CANCEL-REMAINING"),
                    ],
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
