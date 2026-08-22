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

public sealed class ImportsControllerDuplicateAndWarningTests
{
    [Fact]
    public async Task DuplicateUnreadableAndMismatchRemainTyped()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await ImportUiTestSupport.LoginAsync(factory);

        await ImportUiTestSupport.PostFixtureAsync(client, "valid-multi-order-batch.pdf");
        var duplicateLines = await ImportUiTestSupport.PostFixtureAsync(
            client,
            "valid-multi-order-batch.pdf"
        );
        using var duplicate = JsonDocument.Parse(duplicateLines[^1]);

        Assert.Contains(
            duplicate.RootElement.GetProperty("results").EnumerateArray(),
            item => item.GetProperty("failureCode").GetString() == "duplicateOrder"
        );

        var unreadableLines = await ImportUiTestSupport.PostFixtureAsync(
            client,
            "corrupted-file.pdf"
        );
        using var unreadable = JsonDocument.Parse(unreadableLines[^1]);

        Assert.Equal(
            "unreadablePdf",
            unreadable.RootElement.GetProperty("attemptFailureCode").GetString()
        );

        var mismatchLines = await ImportUiTestSupport.PostFixtureAsync(
            client,
            "summary-mismatch.pdf"
        );
        using var mismatch = JsonDocument.Parse(mismatchLines[^1]);

        Assert.Equal(
            "summaryMismatch",
            mismatch.RootElement.GetProperty("attemptFailureCode").GetString()
        );
        Assert.True(mismatch.RootElement.GetProperty("ordersProcessed").GetInt32() > 0);
        Assert.Contains(
            mismatch.RootElement.GetProperty("results").EnumerateArray(),
            item =>
                item.GetProperty("sourceOrderIdentifier").GetString() == "SUM-ACTUAL-IDX"
                && item.GetProperty("outcome").GetString() == "succeeded"
        );

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        Assert.True(
            await context.Orders.AnyAsync(order => order.TcgplayerOrderId == "SUM-ACTUAL-IDX")
        );
    }

    [Fact]
    public async Task SummaryMismatchStillProcessesAndPersistsValidOrders()
    {
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPackingSlipParser>();
                services.AddScoped<IPackingSlipParser, ValidMismatchParser>();
            })
        );
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        using var form = ImportUiTestSupport.FileForm([1]);

        var response = await client.PostAsync("/api/imports", form);
        var lines = (await response.Content.ReadAsStringAsync()).Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries
        );
        using var terminal = JsonDocument.Parse(lines[^1]);

        Assert.Equal(
            "summaryMismatch",
            terminal.RootElement.GetProperty("attemptFailureCode").GetString()
        );
        Assert.Equal(1, terminal.RootElement.GetProperty("ordersProcessed").GetInt32());
        Assert.Contains(
            terminal.RootElement.GetProperty("results").EnumerateArray(),
            item => item.GetProperty("outcome").GetString() == "succeeded"
        );

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        Assert.True(await context.Orders.AnyAsync(order => order.TcgplayerOrderId == "VALID-1"));
    }

    private sealed class ValidMismatchParser : IPackingSlipParser
    {
        public async IAsyncEnumerable<PackingSlipParseUpdate> ParseAsync(
            Stream packingSlipPdf,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.Yield();
            yield return new PackingSlipParseUpdate(
                1,
                true,
                new ParsedPackingSlip
                {
                    OrderBlocks =
                    [
                        new RawOrderBlock
                        {
                            OrderIdentifier = "VALID-1",
                            ProductLines =
                            [
                                new RawProductLine
                                {
                                    QuantityText = "1",
                                    RawDescription =
                                        "Pokemon - Test Set: Test Card - #1 - Common - Near Mint",
                                },
                            ],
                        },
                    ],
                    SummaryPageFound = true,
                    SummaryOrderIdentifiers = ["DIFFERENT-1"],
                }
            );
        }
    }
}
