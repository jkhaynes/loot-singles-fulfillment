using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LootSingles.Api.Controllers;
using LootSingles.Application.CardCatalog;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LootSingles.IntegrationTests.Orders;

public sealed class OrdersControllerTests
{
    [Fact]
    public async Task GetByIdReturnsImageUrlFromRegisteredProviderAndNullForUnsupportedGame()
    {
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddScoped<ICardCatalogProvider>(_ => new FakeCardCatalogProvider(
                    "Pokemon",
                    "https://example.com/genesect-ex.png"
                ))
            )
        );

        Order order;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
            context.Employees.Add(
                new Employee
                {
                    Username = "imageorderuser",
                    NormalizedUsername = "IMAGEORDERUSER",
                    DisplayName = "Image Order User",
                    PinHash = new Pbkdf2PinHasher().Hash("1234"),
                    Role = EmployeeRole.Picker,
                    CreatedAt = DateTimeOffset.UtcNow,
                }
            );
            order = NewOrder("IMAGE-ORDER", DateTimeOffset.Parse("2026-08-24T15:00:00Z"));
            order.OrderLines.Add(
                NewOrderLine("Genesect ex", "SV: Black Bolt", "Holofoil", "Near Mint", 3)
            );
            order.OrderLines.Add(
                NewOrderLine("Lightning Bolt", "Alpha", null, "Near Mint", 1, productLine: "Magic")
            );
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }

        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }
        );
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("imageorderuser", "1234")
        );
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var response = await client.GetAsync($"/api/orders/{order.Id}");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lines = document.RootElement.GetProperty("lines").EnumerateArray().ToArray();
        Assert.Equal(
            "https://example.com/genesect-ex.png",
            lines[0].GetProperty("imageUrl").GetString()
        );
        Assert.True(lines[1].TryGetProperty("imageUrl", out var unsupportedGameImageUrl));
        Assert.Equal(JsonValueKind.Null, unsupportedGameImageUrl.ValueKind);
    }

    private sealed class FakeCardCatalogProvider(string productLine, string imageUrl)
        : ICardCatalogProvider
    {
        public string ProductLine { get; } = productLine;

        public Task<string?> TryMatchImageUrlAsync(
            CardIdentity identity,
            CancellationToken cancellationToken
        ) => Task.FromResult<string?>(imageUrl);
    }

    [Fact]
    public async Task GetByIdWithoutSessionReturns401()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.EnsureDatabaseCreatedAsync();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/orders/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetByIdReturnsFullOrderDetail()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = NewOrder("DETAIL-ORDER", DateTimeOffset.Parse("2026-08-24T15:00:00Z"));
        order.OrderLines.Add(
            NewOrderLine(
                "Genesect ex",
                "SV: Black Bolt",
                "Holofoil",
                "Near Mint",
                3,
                collectorNumber: "#067/086",
                rarity: "Double Rare"
            )
        );
        order.OrderLines.Add(
            NewOrderLine("Pikachu", "Base Set", null, "Lightly Played", 1, rarity: null)
        );
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(order);
            return Task.CompletedTask;
        });

        var response = await client.GetAsync($"/api/orders/{order.Id}");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(order.Id, document.RootElement.GetProperty("orderId").GetInt32());
        Assert.Equal(
            "DETAIL-ORDER",
            document.RootElement.GetProperty("tcgplayerOrderId").GetString()
        );
        Assert.Equal("ready", document.RootElement.GetProperty("status").GetString());
        var lines = document.RootElement.GetProperty("lines").EnumerateArray().ToArray();
        Assert.Collection(
            lines,
            line =>
                AssertOrderLine(
                    line,
                    "Genesect ex",
                    "SV: Black Bolt",
                    "Holofoil",
                    "Near Mint",
                    3,
                    "Pokemon",
                    "#067/086",
                    "Double Rare"
                ),
            line =>
                AssertOrderLine(
                    line,
                    "Pikachu",
                    "Base Set",
                    null,
                    "Lightly Played",
                    1,
                    "Pokemon",
                    "#001",
                    null
                )
        );
        Assert.True(lines[1].TryGetProperty("variant", out var variant));
        Assert.Equal(JsonValueKind.Null, variant.ValueKind);
        Assert.True(lines[1].TryGetProperty("rarity", out var rarity));
        Assert.Equal(JsonValueKind.Null, rarity.ValueKind);
    }

    [Fact]
    public async Task GetByIdForNonExistentOrderReturns404WithOrderNotFoundError()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);

        var response = await client.GetAsync("/api/orders/2147483647");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("order_not_found", document.RootElement.GetProperty("error").GetString());
        Assert.Single(document.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task GetWithoutSessionReturns401()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.EnsureDatabaseCreatedAsync();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetWithNoOrdersReturnsEmptyArray()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);

        var response = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<OrderResponse>>())!);
    }

    [Fact]
    public async Task GetReturnsNarrowProjectionInRequiredOrder()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var newest = DateTimeOffset.Parse("2026-08-22T15:00:00Z");
        var oldest = DateTimeOffset.Parse("2026-08-21T15:00:00Z");
        await factory.SeedAsync(context =>
        {
            context.Orders.AddRange(
                NewOrder("B-ORDER", newest),
                NewOrder("A-ORDER", newest),
                NewOrder("C-ORDER", oldest)
            );
            return Task.CompletedTask;
        });

        var response = await client.GetAsync("/api/orders");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["A-ORDER", "B-ORDER", "C-ORDER"],
            document
                .RootElement.EnumerateArray()
                .Select(item => item.GetProperty("tcgplayerOrderId").GetString()!)
                .ToArray()
        );
        Assert.All(
            document.RootElement.EnumerateArray(),
            item =>
            {
                Assert.Equal(
                    ["importedAt", "orderId", "status", "tcgplayerOrderId"],
                    item.EnumerateObject().Select(property => property.Name).Order().ToArray()
                );
                Assert.Equal("ready", item.GetProperty("status").GetString());
                Assert.DoesNotContain(
                    "orderLines",
                    item.EnumerateObject().Select(property => property.Name)
                );
            }
        );
        Assert.DoesNotContain("customer", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("address", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("importAttempt", json, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpClient> LoginAsync(AuthWebApplicationFactory factory)
    {
        await factory.SeedAsync(context =>
        {
            context.Employees.Add(
                new Employee
                {
                    Username = "ordersuser",
                    NormalizedUsername = "ORDERSUSER",
                    DisplayName = "Orders User",
                    PinHash = new Pbkdf2PinHasher().Hash("1234"),
                    Role = EmployeeRole.Picker,
                    CreatedAt = DateTimeOffset.UtcNow,
                }
            );
            return Task.CompletedTask;
        });
        var client = factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("ordersuser", "1234")
        );
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    private static Order NewOrder(string id, DateTimeOffset importedAt) =>
        new()
        {
            TcgplayerOrderId = id,
            Status = OrderStatus.Ready,
            ImportedAt = importedAt,
        };

    private static OrderLine NewOrderLine(
        string productName,
        string set,
        string? variant,
        string condition,
        int quantity,
        string collectorNumber = "#001",
        string? rarity = null,
        string productLine = "Pokemon"
    ) =>
        new()
        {
            RawDescription = productName,
            ProductLine = productLine,
            ProductName = productName,
            Set = set,
            CollectorNumber = collectorNumber,
            Rarity = rarity,
            Condition = condition,
            Variant = variant,
            Quantity = quantity,
        };

    private static void AssertOrderLine(
        JsonElement line,
        string productName,
        string set,
        string? variant,
        string condition,
        int quantity,
        string productLine,
        string collectorNumber,
        string? rarity
    )
    {
        Assert.Equal(productName, line.GetProperty("productName").GetString());
        Assert.Equal(set, line.GetProperty("set").GetString());
        Assert.Equal(variant, line.GetProperty("variant").GetString());
        Assert.Equal(productLine, line.GetProperty("productLine").GetString());
        Assert.Equal(collectorNumber, line.GetProperty("collectorNumber").GetString());
        Assert.Equal(rarity, line.GetProperty("rarity").GetString());
        Assert.Equal(condition, line.GetProperty("condition").GetString());
        Assert.Equal(quantity, line.GetProperty("quantity").GetInt32());
    }

    private sealed record OrderResponse(
        int OrderId,
        string TcgplayerOrderId,
        OrderStatus Status,
        DateTimeOffset ImportedAt
    );
}
