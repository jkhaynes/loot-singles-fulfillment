using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LootSingles.Api.Controllers;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Persistence;
using LootSingles.Infrastructure.Tcgplayer.Synthetic;
using LootSingles.IntegrationTests.Auth;
using LootSingles.IntegrationTests.ImportUi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    private static WebApplicationFactory<Program> Development(WebApplicationFactory<Program> root) =>
        root.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));

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

    private static JsonElement Terminal(string[] lines) => JsonDocument.Parse(lines[^1]).RootElement;

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
        var stubFirst = Results(
            Terminal(await ImportUiTestSupport.PostTcgplayerAsync(stubClient))
        );
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
}
