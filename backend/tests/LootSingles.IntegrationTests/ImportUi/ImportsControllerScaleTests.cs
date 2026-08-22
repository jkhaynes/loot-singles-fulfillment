using System.Runtime.CompilerServices;
using System.Text.Json;
using LootSingles.Application.Import;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LootSingles.IntegrationTests.ImportUi;

public sealed class ImportsControllerScaleTests
{
    [Fact]
    public async Task TwoHundredOrderPdfTraversesRealParserServiceRepositoryAndDatabase()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await ImportUiTestSupport.LoginAsync(factory);

        var lines = await ImportUiTestSupport.PostFixtureAsync(client, "large-200-order-batch.pdf");
        var snapshots = lines.Select(line => JsonDocument.Parse(line)).ToArray();

        try
        {
            Assert.Equal(201, snapshots.Length);
            Assert.Equal(
                Enumerable.Range(1, 200),
                snapshots[..^1]
                    .Select(snapshot =>
                        snapshot.RootElement.GetProperty("ordersProcessed").GetInt32()
                    )
            );
            Assert.All(
                snapshots[..^1],
                snapshot =>
                    Assert.Equal(
                        "inProgress",
                        snapshot.RootElement.GetProperty("status").GetString()
                    )
            );

            var terminal = snapshots[^1].RootElement;
            Assert.Equal("completed", terminal.GetProperty("status").GetString());
            Assert.Equal(200, terminal.GetProperty("ordersDetected").GetInt32());
            Assert.Equal(200, terminal.GetProperty("ordersProcessed").GetInt32());
            Assert.Equal(200, terminal.GetProperty("succeededCount").GetInt32());
            Assert.Equal(0, terminal.GetProperty("failedCount").GetInt32());
            Assert.Equal(200, terminal.GetProperty("results").GetArrayLength());

            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
            var orders = await context.Orders.Include(order => order.OrderLines).ToListAsync();
            Assert.Equal(200, orders.Count);
            Assert.Equal(200, orders.Select(order => order.TcgplayerOrderId).Distinct().Count());
            Assert.All(orders, order => Assert.Single(order.OrderLines));
        }
        finally
        {
            foreach (var snapshot in snapshots)
            {
                snapshot.Dispose();
            }
        }
    }

    [Fact]
    public async Task TwoHundredOrderTransportEmitsIncreasingProgressAndOneTerminalSnapshot()
    {
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPackingSlipImportService>();
                services.AddScoped<IPackingSlipImportService, TwoHundredOrderService>();
            })
        );
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        using var form = ImportUiTestSupport.FileForm([1]);

        var response = await client.PostAsync("/api/imports", form);
        var lines = (await response.Content.ReadAsStringAsync()).Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries
        );
        var snapshots = lines.Select(line => JsonDocument.Parse(line)).ToArray();

        try
        {
            Assert.Equal(201, snapshots.Length);
            Assert.Equal(
                Enumerable.Range(1, 200),
                snapshots[..^1]
                    .Select(snapshot =>
                        snapshot.RootElement.GetProperty("ordersProcessed").GetInt32()
                    )
            );
            Assert.All(
                snapshots[..^1],
                snapshot =>
                    Assert.Equal(
                        "inProgress",
                        snapshot.RootElement.GetProperty("status").GetString()
                    )
            );
            Assert.Equal("completed", snapshots[^1].RootElement.GetProperty("status").GetString());
            Assert.Equal(200, snapshots[^1].RootElement.GetProperty("succeededCount").GetInt32());
        }
        finally
        {
            foreach (var snapshot in snapshots)
            {
                snapshot.Dispose();
            }
        }
    }

    private sealed class TwoHundredOrderService : IPackingSlipImportService
    {
        public async IAsyncEnumerable<ImportProgressUpdate> ImportAsync(
            Stream packingSlipPdf,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            var attempt = new ImportAttempt { StartedAt = DateTimeOffset.UtcNow };

            for (var index = 1; index <= 200; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();

                yield return new ImportProgressUpdate(
                    index,
                    index - 1,
                    index - 1,
                    0,
                    false,
                    attempt
                );

                attempt.ImportOrderResults.Add(
                    new ImportOrderResult
                    {
                        ImportAttemptId = 0,
                        SourceOrderIdentifier = $"SYNTHETIC-{index:D3}",
                        Outcome = ImportOutcome.Succeeded,
                        ResultingOrderId = index,
                    }
                );

                yield return new ImportProgressUpdate(index, index, index, 0, false, attempt);
            }

            yield return new ImportProgressUpdate(200, 200, 200, 0, true, attempt);
        }
    }
}
