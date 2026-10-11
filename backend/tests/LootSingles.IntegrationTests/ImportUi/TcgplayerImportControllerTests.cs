using System.Net;
using System.Text.Json;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Persistence;
using LootSingles.Infrastructure.Tcgplayer;
using LootSingles.IntegrationTests.Auth;
using LootSingles.IntegrationTests.Import;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LootSingles.IntegrationTests.ImportUi;

/// <summary>
/// T032: <c>POST /api/imports/tcgplayer</c> end to end, over the real TCGplayer handler pipeline
/// with a stub primary handler serving the synthetic fixtures (contracts/import-api.md).
/// </summary>
public sealed class TcgplayerImportControllerTests
{
    private const string Route = "/api/imports/tcgplayer";

    // The fixtures' two orders the shared validation rejects: SYN-0006 (units 2, productCount 5)
    // and SYN-0007 (no product name). The other eight import.
    private static readonly string[] RejectedOrders = ["SYN-0006-A1", "SYN-0007-A1"];

    [Fact]
    public async Task Signed_out_requests_get_401_and_reach_no_TCGplayer()
    {
        await using var root = new AuthWebApplicationFactory();
        var stub = new TcgplayerStubHandler();
        await using var factory = ImportUiTestSupport.WithTcgplayerStub(root, stub);
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }
        );

        var response = await client.PostAsync(Route, content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Full_import_streams_snapshots_and_creates_Ready_orders_matching_the_fixtures()
    {
        await using var root = new AuthWebApplicationFactory();
        var stub = new TcgplayerStubHandler();
        await using var factory = ImportUiTestSupport.WithTcgplayerStub(root, stub);
        using var client = await ImportUiTestSupport.LoginAsync(factory);

        var lines = await ImportUiTestSupport.PostTcgplayerAsync(client);

        // One snapshot per finished order, then the terminal one.
        Assert.Equal(11, lines.Length);
        for (var index = 0; index < 10; index++)
        {
            using var snapshot = JsonDocument.Parse(lines[index]);
            Assert.Equal("inProgress", snapshot.RootElement.GetProperty("status").GetString());
            Assert.Equal(10, snapshot.RootElement.GetProperty("ordersDetected").GetInt32());
            Assert.Equal(index + 1, snapshot.RootElement.GetProperty("ordersProcessed").GetInt32());
        }

        using var terminal = JsonDocument.Parse(lines[^1]);
        var root0 = terminal.RootElement;
        Assert.Equal("completed", root0.GetProperty("status").GetString());
        Assert.Equal(10, root0.GetProperty("ordersProcessed").GetInt32());
        Assert.Equal(8, root0.GetProperty("succeededCount").GetInt32());
        Assert.Equal(2, root0.GetProperty("failedCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, root0.GetProperty("attemptFailureCode").ValueKind);
        var rejected = root0
            .GetProperty("results")
            .EnumerateArray()
            .Where(result => result.GetProperty("outcome").GetString() == "rejected")
            .Select(result => result.GetProperty("sourceOrderIdentifier").GetString())
            .Order()
            .ToArray();
        Assert.Equal(RejectedOrders, rejected);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        var orders = await context.Orders.Include(order => order.OrderLines).ToListAsync();
        Assert.Equal(8, orders.Count);
        Assert.All(
            orders,
            order =>
            {
                Assert.Equal(OrderStatus.Ready, order.Status);
                Assert.Equal(OrderImportSource.TcgplayerApi, order.ImportSource);
            }
        );

        var multiple = orders.Single(order => order.TcgplayerOrderId == "SYN-0002-A1");
        Assert.Equal(3, multiple.OrderLines.Sum(line => line.Quantity));
        Assert.Equal(
            2,
            multiple.OrderLines.Single(line => line.ProductName == "Synthetic Golem").Quantity
        );

        var paged = orders.Single(order => order.TcgplayerOrderId == "SYN-0005-A1");
        Assert.Equal(3, paged.OrderLines.Count);

        var sealedBox = orders
            .Single(order => order.TcgplayerOrderId == "SYN-0009-A1")
            .OrderLines.Single();
        Assert.Equal("Synthetic Booster Box", sealedBox.ProductName);
        Assert.Null(sealedBox.CollectorNumber);
    }

    [Fact]
    public async Task Orders_the_three_filter_search_excludes_are_never_fetched_reported_or_imported()
    {
        var logs = new ImportTestSupport.CapturingLoggerProvider();
        await using var root = new AuthWebApplicationFactory();
        var stub = new TcgplayerStubHandler();
        await using var stubbed = ImportUiTestSupport.WithTcgplayerStub(root, stub);
        await using var factory = stubbed.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Trace);
                    logging.AddProvider(logs);
                })
            )
        );
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        var leaked = TcgplayerStubHandler.LeakedOrderNumbers;

        var first = await ImportUiTestSupport.PostTcgplayerAsync(client);
        var second = await ImportUiTestSupport.PostTcgplayerAsync(client);

        // Every search sends all three filters (FR-004), so the stub, which leaks an order when
        // its filter is missing, never returns one, and nothing is ever requested for them.
        Assert.All(
            stub.Requests.Where(request => request.Route == "search"),
            request =>
            {
                Assert.Contains("orderStatusIds=1,2", request.Query);
                Assert.Contains("pickupStatusIds=1", request.Query);
                Assert.Contains("orderTypeIds=1", request.Query);
            }
        );
        Assert.DoesNotContain(
            stub.Requests,
            request => leaked.Any(number => request.Path.Contains(number))
        );
        // No snapshot counts or lists them, on the first press or as "already imported" on the
        // second.
        foreach (var line in first.Concat(second))
        {
            using var snapshot = JsonDocument.Parse(line);
            Assert.Equal(10, snapshot.RootElement.GetProperty("ordersDetected").GetInt32());
            Assert.All(leaked, number => Assert.DoesNotContain(number, line));
        }
        using (var terminal = JsonDocument.Parse(second[^1]))
        {
            Assert.Equal(10, terminal.RootElement.GetProperty("results").GetArrayLength());
        }
        Assert.All(
            logs.Entries,
            entry => Assert.All(leaked, number => Assert.DoesNotContain(number, entry.Message))
        );
        var stored = await OrderNumbersAsync(factory);
        Assert.Equal(8, stored.Count);
        Assert.Empty(stored.Intersect(leaked));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        Assert.False(
            await context.ImportOrderResults.AnyAsync(result =>
                leaked.Contains(result.SourceOrderIdentifier)
            )
        );
    }

    [Fact]
    public async Task Snapshots_have_exactly_the_shape_of_the_PDF_route()
    {
        await using var root = new AuthWebApplicationFactory();
        var stub = new TcgplayerStubHandler();
        await using var factory = ImportUiTestSupport.WithTcgplayerStub(root, stub);
        using var client = await ImportUiTestSupport.LoginAsync(factory);

        var pdfLines = await ImportUiTestSupport.PostFixtureAsync(
            client,
            "valid-multi-order-batch.pdf"
        );
        var apiLines = await ImportUiTestSupport.PostTcgplayerAsync(client);

        using var pdf = JsonDocument.Parse(pdfLines[^1]);
        using var api = JsonDocument.Parse(apiLines[^1]);
        Assert.Equal(PropertyNames(pdf.RootElement), PropertyNames(api.RootElement));
        Assert.Equal(
            PropertyNames(pdf.RootElement.GetProperty("results")[0]),
            PropertyNames(api.RootElement.GetProperty("results")[0])
        );
    }

    [Fact]
    public async Task A_second_call_reports_every_order_as_a_duplicate_without_fetching_items()
    {
        await using var root = new AuthWebApplicationFactory();
        var stub = new TcgplayerStubHandler();
        await using var factory = ImportUiTestSupport.WithTcgplayerStub(root, stub);
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        // Only the eight that import, so every listed order is already in the application.
        stub.OpenOrderNumbers = stub.OpenOrderNumbers.Except(RejectedOrders).ToArray();
        await ImportUiTestSupport.PostTcgplayerAsync(client);
        var requestsBefore = stub.Requests.Count;

        var lines = await ImportUiTestSupport.PostTcgplayerAsync(client);

        using var terminal = JsonDocument.Parse(lines[^1]);
        Assert.Equal("completed", terminal.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, terminal.RootElement.GetProperty("succeededCount").GetInt32());
        var results = terminal.RootElement.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(8, results.Length);
        Assert.All(
            results,
            result =>
            {
                Assert.Equal("rejected", result.GetProperty("outcome").GetString());
                Assert.Equal("duplicateOrder", result.GetProperty("failureCode").GetString());
                Assert.Equal("Already imported", result.GetProperty("failureMessage").GetString());
            }
        );
        // An order already imported has nothing fetched: the press is manifest and search only.
        Assert.DoesNotContain(
            stub.Requests.Skip(requestsBefore),
            request => request.Route is "details" or "items" or "skus" or "products"
        );
        Assert.Equal(8, await CountOrdersAsync(factory));
    }

    [Fact]
    public async Task An_order_first_imported_from_a_packing_slip_is_a_duplicate()
    {
        await using var root = new AuthWebApplicationFactory();
        var stub = new TcgplayerStubHandler();
        await using var factory = ImportUiTestSupport.WithTcgplayerStub(root, stub);
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        await ImportUiTestSupport.PostFixtureAsync(client, "valid-multi-order-batch.pdf");
        string slipOrder;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
            slipOrder = await context
                .Orders.Where(order => order.ImportSource == OrderImportSource.PackingSlipPdf)
                .Select(order => order.TcgplayerOrderId)
                .FirstAsync();
        }
        var ordersBefore = await CountOrdersAsync(factory);
        stub.OpenOrderNumbers = [slipOrder];

        var lines = await ImportUiTestSupport.PostTcgplayerAsync(client);

        using var terminal = JsonDocument.Parse(lines[^1]);
        var result = Assert.Single(terminal.RootElement.GetProperty("results").EnumerateArray());
        Assert.Equal(slipOrder, result.GetProperty("sourceOrderIdentifier").GetString());
        Assert.Equal("duplicateOrder", result.GetProperty("failureCode").GetString());
        Assert.DoesNotContain(
            stub.Requests,
            request => request.Route is "details" or "items" or "skus" or "products"
        );
        Assert.Equal(ordersBefore, await CountOrdersAsync(factory));
    }

    [Fact]
    public async Task Two_concurrent_calls_import_each_order_exactly_once()
    {
        await using var root = new AuthWebApplicationFactory();
        var stub = new TcgplayerStubHandler();
        await using var factory = ImportUiTestSupport.WithTcgplayerStub(root, stub);
        using var first = await ImportUiTestSupport.LoginAsync(factory);
        using var second = factory.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }
        );
        await LoginAgainAsync(second);

        // Hold SYN-0001's items request in each call until both calls have one in flight. Each
        // call checks every order for "already imported" before it fetches any items, so both
        // have decided every order is new; they then race to insert the same orders.
        var bothArrived = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var heldItems = 0;
        stub.Override = async request =>
        {
            if (
                request.Route == "items"
                && request.Path.Contains(TcgplayerStubHandler.OrderNumber(1))
            )
            {
                if (Interlocked.Increment(ref heldItems) == 2)
                    bothArrived.TrySetResult();
                await bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }

            return null;
        };

        var calls = await Task.WhenAll(
            ImportUiTestSupport.PostTcgplayerAsync(first),
            ImportUiTestSupport.PostTcgplayerAsync(second)
        );

        Assert.Equal(2, heldItems);
        var succeeded = new List<string>();
        foreach (var lines in calls)
        {
            using var terminal = JsonDocument.Parse(lines[^1]);
            Assert.Equal("completed", terminal.RootElement.GetProperty("status").GetString());
            succeeded.AddRange(
                terminal
                    .RootElement.GetProperty("results")
                    .EnumerateArray()
                    .Where(result => result.GetProperty("outcome").GetString() == "succeeded")
                    .Select(result => result.GetProperty("sourceOrderIdentifier").GetString()!)
            );
        }

        Assert.Equal(8, succeeded.Count);
        Assert.Equal(8, succeeded.Distinct().Count());
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        var stored = await context.Orders.Select(order => order.TcgplayerOrderId).ToListAsync();
        Assert.Equal(succeeded.Order(), stored.Order());
    }

    [Fact]
    public async Task An_outage_after_two_orders_fails_the_stream_keeps_them_and_a_retry_imports_the_rest_once()
    {
        await using var root = new AuthWebApplicationFactory();
        var stub = new TcgplayerStubHandler();
        await using var factory = ImportUiTestSupport.WithTcgplayerStub(root, stub);
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        // PageSize is 2: the first batch fetched is SYN-0001 and SYN-0002; TCGplayer fails on the
        // items of every later order.
        stub.Override = request =>
            Task.FromResult(
                request.Route == "items"
                && !request.Path.Contains(TcgplayerStubHandler.OrderNumber(1))
                && !request.Path.Contains(TcgplayerStubHandler.OrderNumber(2))
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : null
            );

        var lines = await ImportUiTestSupport.PostTcgplayerAsync(client);

        using (var terminal = JsonDocument.Parse(lines[^1]))
        {
            var snapshot = terminal.RootElement;
            Assert.Equal("failed", snapshot.GetProperty("status").GetString());
            Assert.Equal(
                "tcgplayerUnavailable",
                snapshot.GetProperty("attemptFailureCode").GetString()
            );
            Assert.Equal(
                "Couldn't reach TCGplayer. Orders already imported are kept. Try again in a few minutes, or upload a packing slip.",
                snapshot.GetProperty("attemptFailureMessage").GetString()
            );
            Assert.Equal(2, snapshot.GetProperty("succeededCount").GetInt32());
        }
        Assert.Equal(
            [TcgplayerStubHandler.OrderNumber(1), TcgplayerStubHandler.OrderNumber(2)],
            await OrderNumbersAsync(factory)
        );

        stub.Override = null;
        var retry = await ImportUiTestSupport.PostTcgplayerAsync(client);

        using var retried = JsonDocument.Parse(retry[^1]);
        Assert.Equal("completed", retried.RootElement.GetProperty("status").GetString());
        Assert.Equal(6, retried.RootElement.GetProperty("succeededCount").GetInt32());
        Assert.Equal(
            2,
            retried
                .RootElement.GetProperty("results")
                .EnumerateArray()
                .Count(result => result.GetProperty("failureCode").GetString() == "duplicateOrder")
        );
        var stored = await OrderNumbersAsync(factory);
        Assert.Equal(8, stored.Count);
        Assert.Equal(stored.Distinct().Count(), stored.Count);
    }

    [Fact]
    public async Task A_401_after_a_token_refresh_ends_the_stream_access_refused()
    {
        await using var root = new AuthWebApplicationFactory();
        var stub = new TcgplayerStubHandler();
        await using var factory = ImportUiTestSupport.WithTcgplayerStub(root, stub);
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        stub.Override = _ =>
            Task.FromResult<HttpResponseMessage?>(
                new HttpResponseMessage(HttpStatusCode.Unauthorized)
            );

        var lines = await ImportUiTestSupport.PostTcgplayerAsync(client);

        using var terminal = JsonDocument.Parse(lines[^1]);
        Assert.Equal("failed", terminal.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "tcgplayerAccessRefused",
            terminal.RootElement.GetProperty("attemptFailureCode").GetString()
        );
        Assert.Equal(
            "TCGplayer refused the store's connection. A manager needs to check the TCGplayer API setup. You can upload a packing slip meanwhile.",
            terminal.RootElement.GetProperty("attemptFailureMessage").GetString()
        );
        // The first token, the refused call, one refresh, the refused retry; nothing after.
        Assert.Equal(
            ["token", "store", "token", "store"],
            stub.Requests.Select(request => request.Route)
        );
        Assert.Equal(0, await CountOrdersAsync(factory));
    }

    [Fact]
    public async Task Without_credentials_the_stream_ends_not_configured_and_TCGplayer_gets_no_request()
    {
        await using var root = new AuthWebApplicationFactory();
        var stub = new TcgplayerStubHandler();
        await using var factory = ImportUiTestSupport.WithTcgplayerStub(
            root,
            stub,
            configured: false
        );
        using var client = await ImportUiTestSupport.LoginAsync(factory);

        var lines = await ImportUiTestSupport.PostTcgplayerAsync(client);

        using var terminal = JsonDocument.Parse(Assert.Single(lines));
        Assert.Equal("failed", terminal.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "tcgplayerNotConfigured",
            terminal.RootElement.GetProperty("attemptFailureCode").GetString()
        );
        Assert.Equal(
            "Getting orders from TCGplayer isn't set up here. Use packing-slip upload instead.",
            terminal.RootElement.GetProperty("attemptFailureMessage").GetString()
        );
        Assert.Equal(0, terminal.RootElement.GetProperty("ordersDetected").GetInt32());
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Every_request_carries_the_User_Agent_never_authorizes_and_only_the_token_is_not_a_GET()
    {
        await using var root = new AuthWebApplicationFactory();
        var stub = new TcgplayerStubHandler();
        await using var factory = ImportUiTestSupport.WithTcgplayerStub(root, stub);
        using var client = await ImportUiTestSupport.LoginAsync(factory);

        await ImportUiTestSupport.PostTcgplayerAsync(client);
        await ImportUiTestSupport.PostTcgplayerAsync(client);

        var requests = stub.Requests;
        Assert.Contains(requests, request => request.Route == "items");
        Assert.All(
            requests,
            request =>
            {
                Assert.Equal(TcgplayerUserAgent.Value, request.UserAgent);
                Assert.DoesNotContain(
                    "/app/authorize",
                    request.Path,
                    StringComparison.OrdinalIgnoreCase
                );
                Assert.Equal(
                    request.Route == "token" ? HttpMethod.Post : HttpMethod.Get,
                    request.Method
                );
            }
        );
        Assert.Matches(
            @"^LootSinglesFulfillment/[^ +]+ \(Loot Investments LLC\)$",
            TcgplayerUserAgent.Value
        );
    }

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).ToArray();

    private static async Task<int> CountOrdersAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        return await context.Orders.CountAsync();
    }

    private static async Task<List<string>> OrderNumbersAsync(
        WebApplicationFactory<Program> factory
    )
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        return await context
            .Orders.OrderBy(order => order.TcgplayerOrderId)
            .Select(order => order.TcgplayerOrderId)
            .ToListAsync();
    }

    private static async Task LoginAgainAsync(HttpClient client)
    {
        var response = await client.PostAsync(
            "/api/auth/login",
            System.Net.Http.Json.JsonContent.Create(
                new LootSingles.Api.Controllers.LoginRequest("importer", "1234")
            )
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
