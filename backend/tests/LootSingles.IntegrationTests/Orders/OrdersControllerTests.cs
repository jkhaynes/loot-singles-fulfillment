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

    // 015-pick-completion T028: POST /api/orders/{orderId}/lines/{lineId}/report-issue.
    [Fact]
    public async Task ReportIssue_WithEveryOtherLineConfirmed_StillMakesTheOrderNeedAttention()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "ISSUE-AMONG-PICKED", 3);
        await ClaimAsync(client, order.Id);
        var lines = order.OrderLines.ToArray();
        foreach (var picked in lines.Take(2))
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.PostAsync(PickUrl(order.Id, picked.Id), null)).StatusCode
            );
        }

        var response = await ReportIssueAsync(
            client,
            order.Id,
            lines[2].Id,
            new { issueType = "CardNotFound", note = "Not in the bin" }
        );
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("needsAttention", document.RootElement.GetProperty("status").GetString());
        var flagged = document.RootElement.GetProperty("lines").EnumerateArray().Last();
        Assert.Equal("hasIssue", flagged.GetProperty("pickOutcome").GetString());
        var issue = flagged.GetProperty("currentIssue");
        Assert.Equal("cardNotFound", issue.GetProperty("issueType").GetString());
        Assert.Equal("Not in the bin", issue.GetProperty("note").GetString());
        Assert.Equal("Orders User", issue.GetProperty("reportedByEmployeeName").GetString());
    }

    [Fact]
    public async Task ReportIssue_TwoFlaggedLines_OrderIsPickedOnlyAfterBothAreResolved()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "TWO-FLAGGED-LINES", 10);
        await ClaimAsync(client, order.Id);
        var lines = order.OrderLines.ToArray();
        foreach (var picked in lines.Take(8))
        {
            await client.PostAsync(PickUrl(order.Id, picked.Id), null);
        }
        await ReportIssueAsync(client, order.Id, lines[8].Id, new { issueType = "CardNotFound" });
        await ReportIssueAsync(client, order.Id, lines[9].Id, new { issueType = "Damaged" });

        var afterFirstResolved = await client.PostAsync(PickUrl(order.Id, lines[8].Id), null);
        using var firstDocument = JsonDocument.Parse(
            await afterFirstResolved.Content.ReadAsStringAsync()
        );
        Assert.Equal("needsAttention", firstDocument.RootElement.GetProperty("status").GetString());

        var afterBothResolved = await client.PostAsync(PickUrl(order.Id, lines[9].Id), null);
        using var secondDocument = JsonDocument.Parse(
            await afterBothResolved.Content.ReadAsStringAsync()
        );
        Assert.Equal("picked", secondDocument.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ReportIssue_SingleLineOrder_ImmediatelyNeedsAttention()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "ISSUE-ONE-LINE", 1);
        await ClaimAsync(client, order.Id);

        var response = await ReportIssueAsync(
            client,
            order.Id,
            order.OrderLines.Single().Id,
            new
            {
                issueType = "InsufficientQuantity",
                requiredQuantity = 3,
                foundQuantity = 1,
            }
        );
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("needsAttention", document.RootElement.GetProperty("status").GetString());
        var issue = document
            .RootElement.GetProperty("lines")
            .EnumerateArray()
            .Single()
            .GetProperty("currentIssue");
        Assert.Equal(3, issue.GetProperty("requiredQuantity").GetInt32());
        Assert.Equal(1, issue.GetProperty("foundQuantity").GetInt32());
        Assert.Equal(JsonValueKind.Null, issue.GetProperty("note").ValueKind);
    }

    [Fact]
    public async Task ReportIssue_UnrecognizedIssueType_Returns400()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "ISSUE-BAD-TYPE", 1);
        await ClaimAsync(client, order.Id);

        var response = await ReportIssueAsync(
            client,
            order.Id,
            order.OrderLines.Single().Id,
            new { issueType = "NotARealIssueType" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReportIssue_OrderClaimedBySomeoneElse_Returns409()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "ISSUE-OTHERS-CLAIM", 1);

        var response = await ReportIssueAsync(
            client,
            order.Id,
            order.OrderLines.Single().Id,
            new { issueType = "CardNotFound" }
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // 015-pick-completion T029: a superseded report stays queryable (FR-011).
    [Fact]
    public async Task ReportIssue_SupersededByALaterOutcome_KeepsTheEarlierReportOnRecord()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "ISSUE-HISTORY", 1);
        await ClaimAsync(client, order.Id);
        var line = order.OrderLines.Single();

        await ReportIssueAsync(
            client,
            order.Id,
            line.Id,
            new { issueType = "CardNotFound", note = "First report" }
        );
        await ReportIssueAsync(
            client,
            order.Id,
            line.Id,
            new { issueType = "Damaged", note = "Second report" }
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsync(PickUrl(order.Id, line.Id), null)).StatusCode
        );

        await factory.SeedAsync(async context =>
        {
            var issues = await context
                .PickingIssues.AsNoTracking()
                .Where(issue => issue.OrderLineId == line.Id)
                .OrderBy(issue => issue.Id)
                .ToListAsync();
            Assert.Equal(2, issues.Count);
            Assert.Equal(PickingIssueType.CardNotFound, issues[0].IssueType);
            Assert.Equal("First report", issues[0].Note);
            Assert.Equal(PickingIssueType.Damaged, issues[1].IssueType);
            var stored = await context.OrderLines.AsNoTracking().SingleAsync(l => l.Id == line.Id);
            Assert.Equal(PickOutcome.Picked, stored.PickOutcome);
            Assert.Null(stored.CurrentPickingIssueId);
        });
    }

    // 015-pick-completion T036: the full release -> re-claim -> revise flow (US1 AC5, SC-007).
    [Fact]
    public async Task FlaggedOrder_SurvivesReleaseAndReclaim_ThenReachesPickedWhenResolved()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "FLAGGED-ROUNDTRIP", 3);
        await ClaimAsync(client, order.Id);
        var lines = order.OrderLines.ToArray();

        await ReportIssueAsync(client, order.Id, lines[0].Id, new { issueType = "CardNotFound" });
        await client.PostAsync(PickUrl(order.Id, lines[1].Id), null);
        await client.PostAsync(PickUrl(order.Id, lines[2].Id), null);

        var release = await client.PostAsync($"/api/orders/{order.Id}/release", null);
        using var releaseDocument = JsonDocument.Parse(await release.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        Assert.Equal(
            "needsAttention",
            releaseDocument.RootElement.GetProperty("status").GetString()
        );

        var reclaim = await client.PostAsync($"/api/orders/{order.Id}/claim", null);
        using var reclaimDocument = JsonDocument.Parse(await reclaim.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, reclaim.StatusCode);
        Assert.Equal(
            "needsAttention",
            reclaimDocument.RootElement.GetProperty("status").GetString()
        );

        var resolve = await client.PostAsync(PickUrl(order.Id, lines[0].Id), null);
        using var resolveDocument = JsonDocument.Parse(await resolve.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        Assert.Equal("picked", resolveDocument.RootElement.GetProperty("status").GetString());
    }

    // 015-pick-completion T055 (branch review BR-003): recording an outcome must not re-run card
    // image enrichment. Images cannot change as a result of a pick, and re-enriching puts one
    // external catalog call per line on the most repeated action in the product.
    [Fact]
    public async Task Pick_DoesNotReRunCardImageEnrichment()
    {
        var countingProvider = new CountingCardCatalogProvider("Pokemon");
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICardCatalogProvider>();
                services.AddScoped<ICardCatalogProvider>(_ => countingProvider);
            })
        );

        var (client, order) = await SeedAndLoginAsync(
            factory,
            "pickenrichuser",
            "PICK-NO-REENRICH"
        );
        await ClaimAsync(client, order.Id);

        var detail = await client.GetAsync($"/api/orders/{order.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var callsAfterOpeningTheOrder = countingProvider.CallCount;
        Assert.True(callsAfterOpeningTheOrder > 0, "Opening the order should enrich its images.");

        var pick = await client.PostAsync(PickUrl(order.Id, order.OrderLines.First().Id), null);
        Assert.Equal(HttpStatusCode.OK, pick.StatusCode);

        Assert.Equal(callsAfterOpeningTheOrder, countingProvider.CallCount);
    }

    [Fact]
    public async Task ReportIssue_DoesNotReRunCardImageEnrichment()
    {
        var countingProvider = new CountingCardCatalogProvider("Pokemon");
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICardCatalogProvider>();
                services.AddScoped<ICardCatalogProvider>(_ => countingProvider);
            })
        );

        var (client, order) = await SeedAndLoginAsync(
            factory,
            "issueenrichuser",
            "ISSUE-NO-REENRICH"
        );
        await ClaimAsync(client, order.Id);

        await client.GetAsync($"/api/orders/{order.Id}");
        var callsAfterOpeningTheOrder = countingProvider.CallCount;

        var reported = await ReportIssueAsync(
            client,
            order.Id,
            order.OrderLines.First().Id,
            new { issueType = "cardNotFound" }
        );
        Assert.Equal(HttpStatusCode.OK, reported.StatusCode);

        Assert.Equal(callsAfterOpeningTheOrder, countingProvider.CallCount);
    }

    /// <summary>
    /// Seeds a picker and a two-line order through a factory customized by
    /// <c>WithWebHostBuilder</c> (which is a plain <see cref="WebApplicationFactory{Program}"/>,
    /// so this file's AuthWebApplicationFactory helpers do not apply) and returns a logged-in
    /// client.
    /// </summary>
    private static async Task<(HttpClient Client, Order Order)> SeedAndLoginAsync(
        WebApplicationFactory<Program> factory,
        string username,
        string tcgplayerOrderId
    )
    {
        var order = NewOrder(tcgplayerOrderId, DateTimeOffset.UtcNow);
        order.OrderLines.Add(NewOrderLine("Card 1", "Base Set", null, "Near Mint", 1));
        order.OrderLines.Add(NewOrderLine("Card 2", "Base Set", null, "Near Mint", 1));

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
                    Role = EmployeeRole.Picker,
                    CreatedAt = DateTimeOffset.UtcNow,
                }
            );
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }

        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }
        );
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(username, "1234")
        );
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (client, order);
    }

    private sealed class CountingCardCatalogProvider(string productLine) : ICardCatalogProvider
    {
        private int _callCount;

        public string ProductLine { get; } = productLine;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<string?> TryMatchImageUrlAsync(
            CardIdentity identity,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult<string?>($"https://example.com/{identity.ProductName}.png");
        }
    }

    // 015-pick-completion T054 (branch review BR-001): oversized or negative issue details are
    // rejected as a typed 400, not by SQL Server truncating and surfacing a 500.
    [Fact]
    public async Task ReportIssue_NoteLongerThanTheColumn_Returns400AndPersistsNothing()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "ISSUE-LONG-NOTE", 1);
        await ClaimAsync(client, order.Id);
        var line = order.OrderLines.Single();

        var response = await ReportIssueAsync(
            client,
            order.Id,
            line.Id,
            new { issueType = "other", note = new string('x', PickingIssue.NoteMaxLength + 1) }
        );
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "invalid_issue_details",
            document.RootElement.GetProperty("error").GetString()
        );
        await AssertNoIssueRecordedAsync(factory, line.Id);
    }

    [Fact]
    public async Task ReportIssue_NegativeQuantity_Returns400AndPersistsNothing()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "ISSUE-NEGATIVE-QTY", 1);
        await ClaimAsync(client, order.Id);
        var line = order.OrderLines.Single();

        var response = await ReportIssueAsync(
            client,
            order.Id,
            line.Id,
            new
            {
                issueType = "insufficientQuantity",
                requiredQuantity = 2,
                foundQuantity = -1,
            }
        );
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "invalid_issue_details",
            document.RootElement.GetProperty("error").GetString()
        );
        await AssertNoIssueRecordedAsync(factory, line.Id);
    }

    [Fact]
    public async Task ReportIssue_NoteExactlyAtTheLimit_IsAccepted()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "ISSUE-LIMIT-NOTE", 1);
        await ClaimAsync(client, order.Id);
        var note = new string('x', PickingIssue.NoteMaxLength);

        var response = await ReportIssueAsync(
            client,
            order.Id,
            order.OrderLines.Single().Id,
            new { issueType = "other", note }
        );
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("needsAttention", document.RootElement.GetProperty("status").GetString());
    }

    private static async Task AssertNoIssueRecordedAsync(
        AuthWebApplicationFactory factory,
        int orderLineId
    )
    {
        await factory.SeedAsync(async context =>
        {
            Assert.False(
                await context.PickingIssues.AnyAsync(issue => issue.OrderLineId == orderLineId)
            );
            var stored = await context
                .OrderLines.AsNoTracking()
                .SingleAsync(line => line.Id == orderLineId);
            Assert.Null(stored.PickOutcome);
        });
    }

    private static Task<HttpResponseMessage> ReportIssueAsync(
        HttpClient client,
        int orderId,
        int lineId,
        object request
    ) => client.PostAsJsonAsync($"/api/orders/{orderId}/lines/{lineId}/report-issue", request);

    // 015-pick-completion T061 (branch review BR-004): spec.md's edge case — a Manager/Admin
    // performing an action reserved for the assigned picker is rejected, with no exemption
    // (FR-010). The gate is claim-based rather than role-based; this pins that it stays so.
    [Fact]
    public async Task Pick_ByManagerWhoDoesNotHoldTheClaim_Returns409AndLeavesLineUnrecorded()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var pickerClient = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "MANAGER-PICK-DENIED", 1);
        await ClaimAsync(pickerClient, order.Id);
        using var managerClient = await LoginAsManagerAsync(factory);
        var line = order.OrderLines.Single();

        var response = await managerClient.PostAsync(PickUrl(order.Id, line.Id), null);
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
    public async Task ReportIssue_ByManagerWhoDoesNotHoldTheClaim_Returns409AndPersistsNothing()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var pickerClient = await LoginAsync(factory);
        var order = await SeedOrderWithLinesAsync(factory, "MANAGER-ISSUE-DENIED", 1);
        await ClaimAsync(pickerClient, order.Id);
        using var managerClient = await LoginAsManagerAsync(factory);
        var line = order.OrderLines.Single();

        var response = await ReportIssueAsync(
            managerClient,
            order.Id,
            line.Id,
            new { issueType = "cardNotFound" }
        );
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("not_your_claim", document.RootElement.GetProperty("error").GetString());
        await AssertNoIssueRecordedAsync(factory, line.Id);
    }

    private static async Task<HttpClient> LoginAsManagerAsync(AuthWebApplicationFactory factory)
    {
        const string username = "ordersmanager";
        await factory.SeedAsync(context =>
        {
            context.Employees.Add(
                new Employee
                {
                    Username = username,
                    NormalizedUsername = username.ToUpperInvariant(),
                    DisplayName = "Orders Manager",
                    PinHash = new Pbkdf2PinHasher().Hash("1234"),
                    Role = EmployeeRole.ManagerAdmin,
                    CreatedAt = DateTimeOffset.UtcNow,
                }
            );
            return Task.CompletedTask;
        });
        var client = factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(username, "1234")
        );
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
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
