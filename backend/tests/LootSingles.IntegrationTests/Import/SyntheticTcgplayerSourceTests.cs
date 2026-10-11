using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LootSingles.Api.Controllers;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Persistence;
using LootSingles.Infrastructure.Tcgplayer;
using LootSingles.Infrastructure.Tcgplayer.Synthetic;
using LootSingles.IntegrationTests.Auth;
using LootSingles.IntegrationTests.ImportUi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace LootSingles.IntegrationTests.Import;

/// <summary>
/// 021 T007: run as Development with no TCGplayer keys configured, "Get new orders" imports the
/// fixed synthetic set through the real import pipeline (FR-001..FR-008).
/// </summary>
public sealed class SyntheticTcgplayerSourceTests
{
    private static readonly string[] ImportedNumbers =
    [
        .. new[] { 1, 2, 3, 4, 5, 8, 9, 10 }.Select(SyntheticTcgplayerHandler.OrderNumber),
    ];

    private static WebApplicationFactory<Program> Development(
        WebApplicationFactory<Program> root
    ) => root.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));

    private static async Task<HttpClient> LoginAsync(
        WebApplicationFactory<Program> factory,
        string username,
        EmployeeRole role
    )
    {
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
            context.Employees.Add(
                new Employee
                {
                    Username = username,
                    NormalizedUsername = username.ToUpperInvariant(),
                    DisplayName = username,
                    PinHash = new Pbkdf2PinHasher().Hash("1234"),
                    Role = role,
                    CreatedAt = DateTimeOffset.UtcNow,
                }
            );
            await context.SaveChangesAsync();
        }

        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "1234"))
            ).StatusCode
        );
        return client;
    }

    private static JsonElement Terminal(string[] lines) =>
        JsonDocument.Parse(lines[^1]).RootElement;

    private static (string Order, string Outcome, string? Code)[] Results(JsonElement terminal) =>
        terminal
            .GetProperty("results")
            .EnumerateArray()
            .Select(r =>
                (
                    r.GetProperty("sourceOrderIdentifier").GetString()!,
                    r.GetProperty("outcome").GetString()!,
                    r.GetProperty("failureCode").GetString()
                )
            )
            .OrderBy(r => r.Item1)
            .ToArray();

    [Theory]
    [InlineData(EmployeeRole.ManagerAdmin)]
    [InlineData(EmployeeRole.Picker)]
    public async Task Get_new_orders_imports_exactly_the_eight_open_synthetic_orders(
        EmployeeRole role
    )
    {
        await using var root = new AuthWebApplicationFactory();
        await using var factory = Development(root);
        using var client = await LoginAsync(factory, "devuser", role);

        var lines = await ImportUiTestSupport.PostTcgplayerAsync(client);

        var terminal = Terminal(lines);
        Assert.Equal("completed", terminal.GetProperty("status").GetString());
        Assert.Equal(8, terminal.GetProperty("succeededCount").GetInt32());
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        var orders = await context.Orders.Include(o => o.OrderLines).ToListAsync();
        Assert.Equal(ImportedNumbers.Order(), orders.Select(o => o.TcgplayerOrderId).Order());
        Assert.All(orders, o => Assert.Equal(OrderImportSource.TcgplayerApi, o.ImportSource));
        // SYN-0011..0013 are not open, so never imported.
        Assert.DoesNotContain(
            orders,
            o => SyntheticTcgplayerHandler.LeakedOrderNumbers.Contains(o.TcgplayerOrderId)
        );
    }

    [Fact]
    public async Task Orders_are_stored_as_the_translator_produces_them()
    {
        await using var root = new AuthWebApplicationFactory();
        await using var factory = Development(root);
        using var client = await LoginAsync(factory, "devuser", EmployeeRole.Picker);
        await ImportUiTestSupport.PostTcgplayerAsync(client);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        var multiple = await context
            .Orders.Include(o => o.OrderLines)
            .SingleAsync(o => o.TcgplayerOrderId == "SYN-0002-A1");
        Assert.Equal(
            2,
            multiple.OrderLines.Single(l => l.ProductName == "Synthetic Golem").Quantity
        );
        var sealedBox = (
            await context
                .Orders.Include(o => o.OrderLines)
                .SingleAsync(o => o.TcgplayerOrderId == "SYN-0009-A1")
        ).OrderLines.Single();
        Assert.Equal("Synthetic Booster Box", sealedBox.ProductName);
        Assert.Null(sealedBox.CollectorNumber);
    }

    [Fact]
    public async Task A_second_press_records_all_eight_as_already_imported_and_creates_nothing()
    {
        await using var root = new AuthWebApplicationFactory();
        await using var factory = Development(root);
        using var client = await LoginAsync(factory, "devuser", EmployeeRole.Picker);
        await ImportUiTestSupport.PostTcgplayerAsync(client);

        var second = Terminal(await ImportUiTestSupport.PostTcgplayerAsync(client));

        Assert.Equal(0, second.GetProperty("succeededCount").GetInt32());
        var results = Results(second);
        Assert.Equal(8, results.Length);
        Assert.All(
            results,
            r =>
            {
                Assert.Equal("rejected", r.Outcome);
                Assert.Equal("duplicateOrder", r.Code);
            }
        );
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        Assert.Equal(8, await context.Orders.CountAsync());
    }

    [Fact]
    public async Task Outcome_codes_match_those_of_a_stubbed_import()
    {
        await using var devRoot = new AuthWebApplicationFactory();
        await using var dev = Development(devRoot);
        using var devClient = await LoginAsync(dev, "devuser", EmployeeRole.Picker);
        var devFirst = Results(Terminal(await ImportUiTestSupport.PostTcgplayerAsync(devClient)));
        var devSecond = Results(Terminal(await ImportUiTestSupport.PostTcgplayerAsync(devClient)));

        await using var stubRoot = new AuthWebApplicationFactory();
        var stub = new SyntheticTcgplayerHandler { OpenOrderNumbers = ImportedNumbers };
        await using var stubbed = ImportUiTestSupport.WithTcgplayerStub(stubRoot, stub);
        using var stubClient = await ImportUiTestSupport.LoginAsync(stubbed);
        var stubFirst = Results(Terminal(await ImportUiTestSupport.PostTcgplayerAsync(stubClient)));
        var stubSecond = Results(
            Terminal(await ImportUiTestSupport.PostTcgplayerAsync(stubClient))
        );

        Assert.Equal(stubFirst, devFirst);
        Assert.Equal(stubSecond, devSecond);
    }

    [Fact]
    public async Task An_imported_order_can_be_claimed_picked_found_at_the_desk_and_packed()
    {
        await using var root = new AuthWebApplicationFactory();
        await using var factory = Development(root);
        using var client = await LoginAsync(factory, "devuser", EmployeeRole.Picker);
        await ImportUiTestSupport.PostTcgplayerAsync(client);
        int orderId;
        int[] lineIds;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
            var order = await context
                .Orders.Include(o => o.OrderLines)
                .SingleAsync(o => o.TcgplayerOrderId == "SYN-0001-A1");
            orderId = order.Id;
            lineIds = order.OrderLines.Select(l => l.Id).ToArray();
        }

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsync($"/api/orders/{orderId}/claim", null)).StatusCode
        );
        foreach (var lineId in lineIds)
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (
                    await client.PostAsync($"/api/orders/{orderId}/lines/{lineId}/pick", null)
                ).StatusCode
            );
        }

        var resolved = await client.GetAsync("/api/packing/orders/SYN-0001-A1");
        using var body = JsonDocument.Parse(await resolved.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        Assert.Equal(orderId, body.RootElement.GetProperty("orderId").GetInt32());
        Assert.True(body.RootElement.GetProperty("canPack").GetBoolean());

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsync($"/api/orders/{orderId}/packed", null)).StatusCode
        );
        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        Assert.Equal(
            OrderStatus.Packed,
            (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Status
        );
    }

    // The primary handler of the TCGplayer client. The HTTP client factory caches the handler chain
    // per client name for its lifetime (default two minutes) and builds the import's client from
    // that same cached entry, so CreateHandler here returns the chain the import used; the tests
    // that inspect recorded requests also assert they are not empty, which proves it is that one.
    private static HttpMessageHandler PrimaryHandler(WebApplicationFactory<Program> factory)
    {
        var handler = factory
            .Services.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(TcgplayerServiceCollectionExtensions.HttpClientName);
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler!;
        return handler;
    }

    // Invented placeholder values only. Built from a prefix so no line pairs a key name with a
    // quoted literal, which the CI credential scan (pr-quality-gate.yml) rightly rejects.
    private static void UsePlaceholderKeys(IWebHostBuilder builder, string prefix)
    {
        builder.UseSetting("Tcgplayer:PublicKey", $"{prefix}-public-key");
        builder.UseSetting("Tcgplayer:PrivateKey", $"{prefix}-private-key");
        builder.UseSetting("Tcgplayer:AccessToken", $"{prefix}-access-token");
    }

    private static WebApplicationFactory<Program> WithEnvironment(
        WebApplicationFactory<Program> root,
        string environment,
        bool syntheticHostWithFakeKeys = false,
        ImportTestSupport.CapturingLoggerProvider? logs = null
    ) =>
        root.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            if (syntheticHostWithFakeKeys)
            {
                UsePlaceholderKeys(builder, "fake");
                builder.UseSetting("Tcgplayer:BaseUrl", SyntheticTcgplayerHandler.BaseUrl);
            }
            if (logs is not null)
                builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    public async Task Outside_Development_with_no_keys_the_import_is_not_configured_and_creates_no_order(
        string environment
    )
    {
        await using var root = new AuthWebApplicationFactory();
        await using var factory = WithEnvironment(root, environment);
        using var client = await LoginAsync(factory, "user", EmployeeRole.Picker);

        var lines = await ImportUiTestSupport.PostTcgplayerAsync(client);

        var terminal = Terminal(lines);
        Assert.Equal("failed", terminal.GetProperty("status").GetString());
        Assert.Equal(
            "tcgplayerNotConfigured",
            terminal.GetProperty("attemptFailureCode").GetString()
        );
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        Assert.Equal(0, await context.Orders.CountAsync());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    public async Task Outside_Development_the_synthetic_handler_is_never_the_primary_handler(
        string environment
    )
    {
        await using var root = new AuthWebApplicationFactory();
        await using var factory = WithEnvironment(
            root,
            environment,
            syntheticHostWithFakeKeys: true
        );

        Assert.IsNotType<SyntheticTcgplayerHandler>(PrimaryHandler(factory));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    public async Task Outside_Development_the_startup_log_does_not_say_synthetic_orders_are_active(
        string environment
    )
    {
        await using var root = new AuthWebApplicationFactory();
        var logs = new ImportTestSupport.CapturingLoggerProvider();
        await using var factory = WithEnvironment(root, environment, logs: logs);

        _ = factory.Services; // builds the host, which is when startup logs

        Assert.DoesNotContain(
            logs.Entries,
            e =>
                e.Message.Contains(
                    "synthetic orders are active",
                    StringComparison.OrdinalIgnoreCase
                )
        );
    }

    [Fact]
    public async Task Development_logs_that_synthetic_orders_are_active()
    {
        await using var root = new AuthWebApplicationFactory();
        var logs = new ImportTestSupport.CapturingLoggerProvider();
        await using var factory = root.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });

        _ = factory.Services;

        Assert.Contains(
            logs.Entries,
            e => e.Message.Contains("synthetic orders are active", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Development_with_real_keys_and_the_real_base_url_configured_still_uses_only_the_synthetic_host()
    {
        await using var root = new AuthWebApplicationFactory();
        // UseSetting lands in the builder's configuration before Program runs, where user secrets
        // and environment variables sit in the real app; a ConfigureAppConfiguration source would
        // arrive after Program has already bound the options and could not stand in for them.
        await using var factory = root.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            UsePlaceholderKeys(builder, "real-looking");
            builder.UseSetting("Tcgplayer:BaseUrl", "https://api.tcgplayer.com/");
        });
        using var client = await LoginAsync(factory, "devuser", EmployeeRole.Picker);

        var options = factory.Services.GetRequiredService<TcgplayerOptions>();
        Assert.Equal(SyntheticTcgplayerHandler.BaseUrl, options.BaseUrl);
        Assert.Equal("synthetic-public-id", options.PublicKey);
        Assert.Equal("synthetic-private-id", options.PrivateKey);
        Assert.Equal("synthetic-store-access", options.AccessToken);

        var terminal = Terminal(await ImportUiTestSupport.PostTcgplayerAsync(client));

        Assert.Equal("completed", terminal.GetProperty("status").GetString());
        Assert.Equal(8, terminal.GetProperty("succeededCount").GetInt32());
        var synthetic = Assert.IsType<SyntheticTcgplayerHandler>(PrimaryHandler(factory));
        var requests = synthetic.Requests;
        Assert.NotEmpty(requests); // the handler inspected is the one the import used
        // The recorded requests carry no host, so the host check is the options above plus the
        // client's base address; the real delegating handlers ran in front (they set the agent).
        Assert.All(requests, r => Assert.Contains("Loot Investments LLC", r.UserAgent));
        var client2 = factory
            .Services.GetRequiredService<IHttpClientFactory>()
            .CreateClient(TcgplayerServiceCollectionExtensions.HttpClientName);
        Assert.Equal(
            SyntheticTcgplayerHandler.BaseUrl,
            client2.BaseAddress!.GetLeftPart(UriPartial.Authority) + "/"
        );
    }
}
