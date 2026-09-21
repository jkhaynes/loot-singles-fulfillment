using System.Net;
using System.Net.Http.Json;
using LootSingles.Api.Controllers;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.IntegrationTests.Auth;

namespace LootSingles.IntegrationTests.Dashboard;

/// <summary>
/// 016-mobile-picking US3: the dashboard tells the signed-in employee which order they already
/// hold, so it can offer to resume it rather than offering to start another (FR-026).
///
/// Every state here is driven through the real claiming and picking endpoints rather than
/// seeded directly, so the tests exercise the same transitions the application performs.
/// </summary>
public class ActiveClaimTests
{
    // T036
    [Fact]
    public async Task GetDashboard_EmployeeHoldsNoOrder_ReturnsNullActiveClaim()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsAsync(factory, "picker", "Picker One");
        await SeedClaimableOrderAsync(factory, "UNHELD-ORDER", lineCount: 2);

        var body = await GetDashboardAsync(client);

        // Holding nothing is an expected state, not an error.
        Assert.Null(body.ActiveClaim);
    }

    // T036
    [Fact]
    public async Task GetDashboard_EmployeeHoldsAnOrder_ReturnsItWithItsCounts()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsAsync(factory, "picker", "Picker One");
        var order = await SeedClaimableOrderAsync(factory, "HELD-ORDER", lineCount: 2);

        var claim = await client.PostAsync($"/api/orders/{order.Id}/claim", null);
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);

        var body = await GetDashboardAsync(client);

        Assert.NotNull(body.ActiveClaim);
        Assert.Equal(order.Id, body.ActiveClaim!.OrderId);
        Assert.Equal("HELD-ORDER", body.ActiveClaim.TcgplayerOrderId);
        Assert.Equal(2, body.ActiveClaim.ProductCount);
        Assert.Equal(2, body.ActiveClaim.TotalQuantity);
    }

    // T036
    [Fact]
    public async Task GetDashboard_AfterReleasingTheOrder_ReturnsNullAgain()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsAsync(factory, "picker", "Picker One");
        var order = await SeedClaimableOrderAsync(factory, "RELEASED-ORDER", lineCount: 1);

        await client.PostAsync($"/api/orders/{order.Id}/claim", null);
        await client.PostAsync($"/api/orders/{order.Id}/release", null);

        Assert.Null((await GetDashboardAsync(client)).ActiveClaim);
    }

    /// <summary>
    /// T037 — the case an "is it in the In Progress section?" implementation would silently get
    /// wrong. Reporting an issue moves the order to Needs Attention but the claim is retained
    /// (015-pick-completion), so the picker still holds it and still needs sending back to it.
    /// </summary>
    [Fact]
    public async Task GetDashboard_HeldOrderSittingInNeedsAttention_StillReturnsIt()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsAsync(factory, "picker", "Picker One");
        var order = await SeedClaimableOrderAsync(factory, "FLAGGED-ORDER", lineCount: 2);

        await client.PostAsync($"/api/orders/{order.Id}/claim", null);
        var lineId = await FirstLineIdAsync(factory, order.Id);
        var report = await client.PostAsJsonAsync(
            $"/api/orders/{order.Id}/lines/{lineId}/report-issue",
            new
            {
                issueType = "cardNotFound",
                requiredQuantity = 1,
                foundQuantity = 0,
                note = (string?)null,
            }
        );
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);

        var body = await GetDashboardAsync(client);

        // It is not in In Progress any more...
        Assert.DoesNotContain(body.InProgress.Orders, summary => summary.OrderId == order.Id);
        Assert.Contains(body.NeedsAttention.Orders, summary => summary.OrderId == order.Id);
        // ...but the employee still holds it.
        Assert.NotNull(body.ActiveClaim);
        Assert.Equal(order.Id, body.ActiveClaim!.OrderId);
    }

    /// <summary>
    /// T037 — the same for a fully picked order, which also keeps its claim
    /// (015-pick-completion, Product Owner decision 2026-09-19).
    /// </summary>
    [Fact]
    public async Task GetDashboard_HeldOrderFullyPicked_StillReturnsIt()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsAsync(factory, "picker", "Picker One");
        var order = await SeedClaimableOrderAsync(factory, "PICKED-ORDER", lineCount: 1);

        await client.PostAsync($"/api/orders/{order.Id}/claim", null);
        var lineId = await FirstLineIdAsync(factory, order.Id);
        var pick = await client.PostAsync($"/api/orders/{order.Id}/lines/{lineId}/pick", null);
        Assert.Equal(HttpStatusCode.OK, pick.StatusCode);

        var body = await GetDashboardAsync(client);

        Assert.Contains(body.Picked.Orders, summary => summary.OrderId == order.Id);
        Assert.NotNull(body.ActiveClaim);
        Assert.Equal(order.Id, body.ActiveClaim!.OrderId);
    }

    /// <summary>
    /// T038 — one employee's dashboard never reports another employee's order, and never carries
    /// their identity (Constitution VII, PRD §27).
    /// </summary>
    [Fact]
    public async Task GetDashboard_AnotherEmployeeHoldsTheOrder_ReturnsNullActiveClaim()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var holder = await LoginAsAsync(factory, "holder", "Holder");
        var order = await SeedClaimableOrderAsync(factory, "SOMEONE-ELSES", lineCount: 1);
        await holder.PostAsync($"/api/orders/{order.Id}/claim", null);

        using var other = await LoginAsAsync(factory, "onlooker", "Onlooker");
        var body = await GetDashboardAsync(other);

        // The order is visibly in progress to everyone; whose it is, is not this employee's business.
        Assert.Contains(body.InProgress.Orders, summary => summary.OrderId == order.Id);
        Assert.Null(body.ActiveClaim);
    }

    private static async Task<DashboardResponse> GetDashboardAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>();
        Assert.NotNull(body);
        return body!;
    }

    private static async Task<int> FirstLineIdAsync(AuthWebApplicationFactory factory, int orderId)
    {
        var lineId = 0;
        await factory.SeedAsync(context =>
        {
            lineId = context.OrderLines.Where(line => line.OrderId == orderId).Min(line => line.Id);
            return Task.CompletedTask;
        });
        return lineId;
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
            OrderLines = Enumerable
                .Range(1, lineCount)
                .Select(_ => new OrderLine
                {
                    RawDescription = "Pikachu - Base Set - #58/102 - Common - Near Mint",
                    ProductLine = "Pokemon",
                    ProductName = "Pikachu",
                    Set = "Base Set",
                    CollectorNumber = "#58/102",
                    Condition = "Near Mint",
                    Quantity = 1,
                })
                .ToList(),
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
        string username,
        string displayName
    )
    {
        var hasher = new Pbkdf2PinHasher();
        await factory.SeedAsync(context =>
        {
            context.Employees.Add(
                new Employee
                {
                    Username = username,
                    NormalizedUsername = username.ToUpperInvariant(),
                    DisplayName = displayName,
                    PinHash = hasher.Hash("1234"),
                    Role = EmployeeRole.Picker,
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
}
