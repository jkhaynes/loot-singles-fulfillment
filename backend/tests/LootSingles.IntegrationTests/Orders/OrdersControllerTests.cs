using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LootSingles.Api.Controllers;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.IntegrationTests.Auth;

namespace LootSingles.IntegrationTests.Orders;

public sealed class OrdersControllerTests
{
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

    private sealed record OrderResponse(
        int OrderId,
        string TcgplayerOrderId,
        OrderStatus Status,
        DateTimeOffset ImportedAt
    );
}
