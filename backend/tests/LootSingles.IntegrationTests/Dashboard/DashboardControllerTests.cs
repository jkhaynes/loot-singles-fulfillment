using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LootSingles.Api.Controllers;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.IntegrationTests.Auth;

namespace LootSingles.IntegrationTests.Dashboard;

public class DashboardControllerTests
{
    [Fact]
    public async Task GetDashboard_NoSession_ReturnsUnauthorized()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.EnsureDatabaseCreatedAsync();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_NoReadyOrders_ReturnsEmptyReadyList()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsAsync(factory, EmployeeRole.Picker);

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>();
        Assert.Equal(0, body?.Ready.Count);
        Assert.Empty(body!.Ready.Orders);
    }

    [Fact]
    public async Task GetDashboard_ReadyOrdersExist_ReturnsCorrectSummaries()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsAsync(factory, EmployeeRole.ManagerAdmin);
        await SeedReadyOrderAsync(factory, "F0000001-ABC001-00001", quantities: [2, 3]);

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>();
        Assert.Equal(1, body?.Ready.Count);
        var order = Assert.Single(body!.Ready.Orders);
        Assert.Equal("F0000001-ABC001-00001", order.TcgplayerOrderId);
        Assert.Equal(2, order.ProductCount);
        Assert.Equal(5, order.TotalQuantity);
    }

    // 015-pick-completion T038: the three sections reflect real order state (US3 AC1, AC2).
    [Fact]
    public async Task GetDashboard_ReflectsRealInProgressNeedsAttentionAndPickedOrders()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsAsync(factory, EmployeeRole.Picker);
        await SeedReadyOrderAsync(factory, "READY-ORDER", quantities: [1]);
        var inProgress = await SeedClaimableOrderAsync(factory, "IN-PROGRESS-ORDER", 2);
        var flagged = await SeedClaimableOrderAsync(factory, "FLAGGED-ORDER", 2);

        // Drive the states through the real picking endpoints rather than seeding them directly.
        await client.PostAsync($"/api/orders/{flagged.Id}/claim", null);
        await client.PostAsJsonAsync(
            $"/api/orders/{flagged.Id}/lines/{flagged.OrderLines.First().Id}/report-issue",
            new { issueType = "cardNotFound" }
        );
        await client.PostAsync($"/api/orders/{flagged.Id}/release", null);
        await client.PostAsync($"/api/orders/{inProgress.Id}/claim", null);

        var response = await client.GetAsync("/api/dashboard");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("ready").GetProperty("count").GetInt32());
        Assert.Equal(1, root.GetProperty("inProgress").GetProperty("count").GetInt32());
        Assert.Equal(
            "IN-PROGRESS-ORDER",
            root.GetProperty("inProgress")
                .GetProperty("orders")
                .EnumerateArray()
                .Single()
                .GetProperty("tcgplayerOrderId")
                .GetString()
        );
        var needsAttention = root.GetProperty("needsAttention");
        Assert.Equal(1, needsAttention.GetProperty("count").GetInt32());
        var flaggedSummary = needsAttention.GetProperty("orders").EnumerateArray().Single();
        Assert.Equal(
            "FLAGGED-ORDER",
            flaggedSummary.GetProperty("tcgplayerOrderId").GetString()
        );
        Assert.Equal(
            ["Pikachu"],
            flaggedSummary
                .GetProperty("flaggedProductNames")
                .EnumerateArray()
                .Select(name => name.GetString())
                .ToArray()
        );
        Assert.Equal(0, root.GetProperty("picked").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task GetDashboard_CountsAFullyConfirmedOrderAsPicked()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsAsync(factory, EmployeeRole.Picker);
        var order = await SeedClaimableOrderAsync(factory, "PICKED-ORDER", 2);
        await client.PostAsync($"/api/orders/{order.Id}/claim", null);
        foreach (var line in order.OrderLines)
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (
                    await client.PostAsync(
                        $"/api/orders/{order.Id}/lines/{line.Id}/pick",
                        content: null
                    )
                ).StatusCode
            );
        }

        var response = await client.GetAsync("/api/dashboard");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var picked = document.RootElement.GetProperty("picked");
        Assert.Equal(1, picked.GetProperty("count").GetInt32());
        Assert.Equal(
            "PICKED-ORDER",
            picked
                .GetProperty("orders")
                .EnumerateArray()
                .Single()
                .GetProperty("tcgplayerOrderId")
                .GetString()
        );
        Assert.Equal(0, document.RootElement.GetProperty("inProgress").GetProperty("count").GetInt32());
    }

    private static async Task<Order> SeedClaimableOrderAsync(
        AuthWebApplicationFactory factory,
        string tcgplayerOrderId,
        int lineCount
    )
    {
        var order = new Order
        {
            TcgplayerOrderId = tcgplayerOrderId,
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow,
            OrderLines = Enumerable.Range(1, lineCount).Select(_ => NewLine(1)).ToList(),
        };
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(order);
            return Task.CompletedTask;
        });
        return order;
    }

    private static async Task<HttpClient> LoginAsAsync(
        AuthWebApplicationFactory factory,
        EmployeeRole role
    )
    {
        var hasher = new Pbkdf2PinHasher();
        var employee = new Employee
        {
            Username = "dashboarduser",
            NormalizedUsername = "DASHBOARDUSER",
            DisplayName = "Dashboard User",
            PinHash = hasher.Hash("1234"),
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await factory.SeedAsync(context =>
        {
            context.Employees.Add(employee);
            return Task.CompletedTask;
        });

        var client = factory.CreateAuthenticatedClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("dashboarduser", "1234")
        );
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        return client;
    }

    private static async Task SeedReadyOrderAsync(
        AuthWebApplicationFactory factory,
        string tcgplayerOrderId,
        int[] quantities
    )
    {
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(
                new Order
                {
                    TcgplayerOrderId = tcgplayerOrderId,
                    Status = OrderStatus.Ready,
                    ImportedAt = DateTimeOffset.UtcNow,
                    OrderLines = quantities.Select(NewLine).ToList(),
                }
            );
            return Task.CompletedTask;
        });
    }

    private static OrderLine NewLine(int quantity) =>
        new()
        {
            RawDescription = "Pikachu - Base Set - #58/102 - Common - Near Mint",
            ProductLine = "Pokemon",
            ProductName = "Pikachu",
            Set = "Base Set",
            CollectorNumber = "#58/102",
            Condition = "Near Mint",
            Quantity = quantity,
        };

    private sealed record DashboardResponse(ReadySectionResponse Ready);

    private sealed record ReadySectionResponse(int Count, List<OrderSummaryResponse> Orders);

    private sealed record OrderSummaryResponse(
        int OrderId,
        string TcgplayerOrderId,
        int ProductCount,
        int TotalQuantity
    );
}
