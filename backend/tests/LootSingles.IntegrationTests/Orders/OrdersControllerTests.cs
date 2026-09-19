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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LootSingles.IntegrationTests.Orders;

public sealed class OrdersControllerTests
{
    [Fact]
    public async Task GetByIdReturnsImageUrlFromRegisteredProviderAndNullForUnsupportedGame()
    {
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICardCatalogProvider>();
                services.AddScoped<ICardCatalogProvider>(_ => new FakeCardCatalogProvider(
                    "Pokemon",
                    "https://example.com/genesect-ex.png"
                ));
            })
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
    public async Task GetByIdWhenProviderThrows_StillReturns200WithNullImageUrlForThatLine()
    {
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICardCatalogProvider>();
                services.AddScoped<ICardCatalogProvider>(_ => new ThrowingCardCatalogProvider(
                    "Pokemon"
                ));
            })
        );

        Order order;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
            context.Employees.Add(
                new Employee
                {
                    Username = "throwingprovideruser",
                    NormalizedUsername = "THROWINGPROVIDERUSER",
                    DisplayName = "Throwing Provider User",
                    PinHash = new Pbkdf2PinHasher().Hash("1234"),
                    Role = EmployeeRole.Picker,
                    CreatedAt = DateTimeOffset.UtcNow,
                }
            );
            order = NewOrder("PROVIDER-THROWS-ORDER", DateTimeOffset.Parse("2026-08-25T15:00:00Z"));
            order.OrderLines.Add(
                NewOrderLine("Genesect ex", "SV: Black Bolt", "Holofoil", "Near Mint", 1)
            );
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }

        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }
        );
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("throwingprovideruser", "1234")
        );
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var response = await client.GetAsync($"/api/orders/{order.Id}");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lines = document.RootElement.GetProperty("lines").EnumerateArray().ToArray();
        Assert.True(lines[0].TryGetProperty("imageUrl", out var imageUrl));
        Assert.Equal(JsonValueKind.Null, imageUrl.ValueKind);
    }

    private sealed class ThrowingCardCatalogProvider(string productLine) : ICardCatalogProvider
    {
        public string ProductLine { get; } = productLine;

        public Task<string?> TryMatchImageUrlAsync(
            CardIdentity identity,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Simulated provider failure.");
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
        Assert.True(
            document.RootElement.TryGetProperty("claimedByEmployeeId", out var claimedByEmployeeId)
        );
        Assert.Equal(JsonValueKind.Null, claimedByEmployeeId.ValueKind);
        Assert.True(
            document.RootElement.TryGetProperty(
                "claimedByEmployeeName",
                out var claimedByEmployeeName
            )
        );
        Assert.Equal(JsonValueKind.Null, claimedByEmployeeName.ValueKind);
    }

    [Fact]
    public async Task GetByIdIncludesClaimStateForAClaimedOrder()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = NewOrder("CLAIMED-DETAIL-ORDER", DateTimeOffset.Parse("2026-08-24T15:00:00Z"));
        order.OrderLines.Add(
            NewOrderLine("Pikachu", "Base Set", null, "Near Mint", 1, rarity: "Common")
        );
        var claimant = new Employee
        {
            Username = "claimantuser",
            NormalizedUsername = "CLAIMANTUSER",
            DisplayName = "Claimant User",
            PinHash = new Pbkdf2PinHasher().Hash("1234"),
            Role = EmployeeRole.Picker,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await factory.SeedAsync(context =>
        {
            context.Employees.Add(claimant);
            context.Orders.Add(order);
            return Task.CompletedTask;
        });
        await factory.SeedAsync(context =>
        {
            order.Status = OrderStatus.InProgress;
            order.ClaimedByEmployeeId = claimant.Id;
            order.ClaimedAt = DateTimeOffset.UtcNow;
            context.Orders.Update(order);
            return Task.CompletedTask;
        });

        var response = await client.GetAsync($"/api/orders/{order.Id}");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("inProgress", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            claimant.Id,
            document.RootElement.GetProperty("claimedByEmployeeId").GetInt32()
        );
        Assert.Equal(
            "Claimant User",
            document.RootElement.GetProperty("claimedByEmployeeName").GetString()
        );
    }

    [Fact]
    public async Task GetIncludesClaimStateForClaimedAndUnclaimedOrders()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var claimant = new Employee
        {
            Username = "listclaimantuser",
            NormalizedUsername = "LISTCLAIMANTUSER",
            DisplayName = "List Claimant",
            PinHash = new Pbkdf2PinHasher().Hash("1234"),
            Role = EmployeeRole.Picker,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var claimed = NewOrder("LIST-CLAIMED-ORDER", DateTimeOffset.UtcNow);
        var unclaimed = NewOrder("LIST-UNCLAIMED-ORDER", DateTimeOffset.UtcNow);
        await factory.SeedAsync(context =>
        {
            context.Employees.Add(claimant);
            context.Orders.AddRange(claimed, unclaimed);
            return Task.CompletedTask;
        });
        await factory.SeedAsync(context =>
        {
            claimed.Status = OrderStatus.InProgress;
            claimed.ClaimedByEmployeeId = claimant.Id;
            claimed.ClaimedAt = DateTimeOffset.UtcNow;
            context.Orders.Update(claimed);
            return Task.CompletedTask;
        });

        var response = await client.GetAsync("/api/orders");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = document.RootElement.EnumerateArray().ToArray();
        var claimedItem = items.Single(item =>
            item.GetProperty("tcgplayerOrderId").GetString() == "LIST-CLAIMED-ORDER"
        );
        var unclaimedItem = items.Single(item =>
            item.GetProperty("tcgplayerOrderId").GetString() == "LIST-UNCLAIMED-ORDER"
        );

        Assert.Equal(claimant.Id, claimedItem.GetProperty("claimedByEmployeeId").GetInt32());
        Assert.Equal("List Claimant", claimedItem.GetProperty("claimedByEmployeeName").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            unclaimedItem.GetProperty("claimedByEmployeeId").ValueKind
        );
        Assert.Equal(
            JsonValueKind.Null,
            unclaimedItem.GetProperty("claimedByEmployeeName").ValueKind
        );
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
                    [
                        "claimedByEmployeeId",
                        "claimedByEmployeeName",
                        "importedAt",
                        "orderId",
                        "status",
                        "tcgplayerOrderId",
                    ],
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

    // 015-pick-completion T015: POST /api/orders/{orderId}/lines/{lineId}/pick.
    [Fact]
    public async Task Pick_ClaimHolderConfirmsEveryLine_OrderBecomesPickedOnlyAfterTheLast()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "PICK-TWO-LINES", 2);
        await ClaimAsync(client, order.Id);
        var first = order.OrderLines.First();
        var second = order.OrderLines.Last();

        var firstResponse = await client.PostAsync(PickUrl(order.Id, first.Id), null);
        using var firstDocument = JsonDocument.Parse(
            await firstResponse.Content.ReadAsStringAsync()
        );

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal("inProgress", firstDocument.RootElement.GetProperty("status").GetString());
        var lines = firstDocument.RootElement.GetProperty("lines").EnumerateArray().ToArray();
        Assert.Equal(first.Id, lines[0].GetProperty("id").GetInt32());
        Assert.Equal("picked", lines[0].GetProperty("pickOutcome").GetString());
        Assert.Equal(JsonValueKind.Null, lines[1].GetProperty("pickOutcome").ValueKind);

        var secondResponse = await client.PostAsync(PickUrl(order.Id, second.Id), null);
        using var secondDocument = JsonDocument.Parse(
            await secondResponse.Content.ReadAsStringAsync()
        );

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal("picked", secondDocument.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Pick_RecordsWhoPickedTheLineAndWhen()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "PICK-ATTRIBUTION", 1);
        await ClaimAsync(client, order.Id);
        var line = order.OrderLines.Single();
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        var response = await client.PostAsync(PickUrl(order.Id, line.Id), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await factory.SeedAsync(async context =>
        {
            var picker = await context.Employees.SingleAsync(e => e.Username == "ordersuser");
            var stored = await context.OrderLines.AsNoTracking().SingleAsync(l => l.Id == line.Id);
            Assert.Equal(PickOutcome.Picked, stored.PickOutcome);
            Assert.Equal(picker.Id, stored.PickOutcomeRecordedByEmployeeId);
            Assert.True(stored.PickOutcomeRecordedAt >= before);
            Assert.Null(stored.CurrentPickingIssueId);
        });
    }

    [Fact]
    public async Task Pick_SingleLineOrder_ImmediatelyBecomesPicked()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "PICK-ONE-LINE", 1);
        await ClaimAsync(client, order.Id);

        var response = await client.PostAsync(
            PickUrl(order.Id, order.OrderLines.Single().Id),
            null
        );
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("picked", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Pick_LineNotInThisOrder_Returns404LineNotFound()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "PICK-OWN-ORDER", 1);
        var otherOrder = await SeedOrderWithLinesAsync(factory, "PICK-OTHER-ORDER", 1);
        await ClaimAsync(client, order.Id);

        var response = await client.PostAsync(
            PickUrl(order.Id, otherOrder.OrderLines.Single().Id),
            null
        );
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("line_not_found", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Pick_OrderDoesNotExist_Returns404OrderNotFound()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);

        var response = await client.PostAsync(PickUrl(2147483647, 1), null);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("order_not_found", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Pick_OrderClaimedBySomeoneElse_Returns409AndLeavesLineUnrecorded()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "PICK-OTHERS-CLAIM", 1);
        await factory.SeedAsync(async context =>
        {
            var claimant = new Employee
            {
                Username = "otherclaimant",
                NormalizedUsername = "OTHERCLAIMANT",
                DisplayName = "Other Claimant",
                PinHash = "hash",
                Role = EmployeeRole.Picker,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            context.Employees.Add(claimant);
            await context.SaveChangesAsync();
            var stored = await context.Orders.SingleAsync(o => o.Id == order.Id);
            stored.ClaimedByEmployeeId = claimant.Id;
            stored.ClaimedAt = DateTimeOffset.UtcNow;
            stored.Status = OrderStatus.InProgress;
        });
        var line = order.OrderLines.Single();

        var response = await client.PostAsync(PickUrl(order.Id, line.Id), null);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("not_your_claim", document.RootElement.GetProperty("error").GetString());
        await factory.SeedAsync(async context =>
        {
            var stored = await context.OrderLines.AsNoTracking().SingleAsync(l => l.Id == line.Id);
            Assert.Null(stored.PickOutcome);
        });
    }

    [Fact]
    public async Task Pick_UnclaimedOrder_Returns409NotYourClaim()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "PICK-UNCLAIMED", 1);

        var response = await client.PostAsync(
            PickUrl(order.Id, order.OrderLines.Single().Id),
            null
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Pick_WithoutSession_Returns401()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.EnsureDatabaseCreatedAsync();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsync(PickUrl(1, 1), null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string PickUrl(int orderId, int lineId) =>
        $"/api/orders/{orderId}/lines/{lineId}/pick";

    private static async Task ClaimAsync(HttpClient client, int orderId)
    {
        var claim = await client.PostAsync($"/api/orders/{orderId}/claim", null);
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
    }

    private static async Task<Order> SeedOrderWithLinesAsync(
        AuthWebApplicationFactory factory,
        string tcgplayerOrderId,
        int lineCount
    )
    {
        var order = NewOrder(tcgplayerOrderId, DateTimeOffset.UtcNow);
        for (var i = 1; i <= lineCount; i++)
        {
            order.OrderLines.Add(NewOrderLine($"Card {i}", "Base Set", null, "Near Mint", 1));
        }
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(order);
            return Task.CompletedTask;
        });
        return order;
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
