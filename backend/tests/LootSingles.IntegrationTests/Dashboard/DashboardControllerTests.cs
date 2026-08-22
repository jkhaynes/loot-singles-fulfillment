using System.Net;
using System.Net.Http.Json;
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
