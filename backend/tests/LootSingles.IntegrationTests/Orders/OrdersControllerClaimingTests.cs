using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LootSingles.Api.Controllers;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.IntegrationTests.Auth;

namespace LootSingles.IntegrationTests.Orders;

public sealed class OrdersControllerClaimingTests
{
    [Fact]
    public async Task PickNext_OrderAvailable_ClaimsOldestReadyOrderAndReturns200()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "pickerone");
        var older = NewOrder("OLDER-ORDER", DateTimeOffset.Parse("2026-08-20T12:00:00Z"));
        var newer = NewOrder("NEWER-ORDER", DateTimeOffset.Parse("2026-08-21T12:00:00Z"));
        await factory.SeedAsync(context =>
        {
            context.Orders.AddRange(newer, older);
            return Task.CompletedTask;
        });

        var response = await client.PostAsync("/api/orders/pick-next", content: null);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(older.Id, document.RootElement.GetProperty("orderId").GetInt32());
        Assert.Equal(
            "OLDER-ORDER",
            document.RootElement.GetProperty("tcgplayerOrderId").GetString()
        );
        Assert.Equal("inProgress", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            employee.Id,
            document.RootElement.GetProperty("claimedByEmployeeId").GetInt32()
        );
        Assert.Equal(
            employee.DisplayName,
            document.RootElement.GetProperty("claimedByEmployeeName").GetString()
        );
    }

    [Fact]
    public async Task PickNext_NoOrdersAvailable_Returns409WithClearError()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, _) = await LoginAsync(factory, "pickertwo");

        var response = await client.PostAsync("/api/orders/pick-next", content: null);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("no_orders_available", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task PickNext_EmployeeAlreadyHasActiveClaim_Returns409WithConflictingOrderId()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "pickerthree");
        var alreadyClaimed = NewOrder("ALREADY-CLAIMED", DateTimeOffset.UtcNow);
        alreadyClaimed.Status = OrderStatus.InProgress;
        alreadyClaimed.ClaimedByEmployeeId = employee.Id;
        alreadyClaimed.ClaimedAt = DateTimeOffset.UtcNow;
        var available = NewOrder("AVAILABLE-ORDER", DateTimeOffset.UtcNow);
        await factory.SeedAsync(context =>
        {
            context.Orders.AddRange(alreadyClaimed, available);
            return Task.CompletedTask;
        });

        var response = await client.PostAsync("/api/orders/pick-next", content: null);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "employee_has_active_claim",
            document.RootElement.GetProperty("error").GetString()
        );
        Assert.Equal(
            alreadyClaimed.Id,
            document.RootElement.GetProperty("claimedOrderId").GetInt32()
        );
    }

    [Fact]
    public async Task Claim_OrderAvailable_ClaimsItAndReturns200()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "chooserone");
        var target = NewOrder("CHOOSE-ORDER", DateTimeOffset.UtcNow);
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(target);
            return Task.CompletedTask;
        });

        var response = await client.PostAsync($"/api/orders/{target.Id}/claim", content: null);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(target.Id, document.RootElement.GetProperty("orderId").GetInt32());
        Assert.Equal("inProgress", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            employee.Id,
            document.RootElement.GetProperty("claimedByEmployeeId").GetInt32()
        );
    }

    [Fact]
    public async Task Claim_OrderDoesNotExist_Returns404()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, _) = await LoginAsync(factory, "choosertwo");

        var response = await client.PostAsync("/api/orders/2147483647/claim", content: null);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("order_not_found", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Claim_OrderAlreadyClaimedByAnother_Returns409WithClaimantIdentity()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (firstClient, firstEmployee) = await LoginAsync(factory, "chooserthree");
        var (secondClient, _) = await LoginAsync(factory, "chooserfour");
        var target = NewOrder("CONTESTED-ORDER", DateTimeOffset.UtcNow);
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(target);
            return Task.CompletedTask;
        });
        var firstClaim = await firstClient.PostAsync(
            $"/api/orders/{target.Id}/claim",
            content: null
        );
        Assert.Equal(HttpStatusCode.OK, firstClaim.StatusCode);

        var response = await secondClient.PostAsync(
            $"/api/orders/{target.Id}/claim",
            content: null
        );
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "order_already_claimed",
            document.RootElement.GetProperty("error").GetString()
        );
        Assert.Equal(
            firstEmployee.Id,
            document.RootElement.GetProperty("claimedByEmployeeId").GetInt32()
        );
        Assert.Equal(
            firstEmployee.DisplayName,
            document.RootElement.GetProperty("claimedByEmployeeName").GetString()
        );
    }

    [Fact]
    public async Task Claim_EmployeeAlreadyHasActiveClaim_Returns409WithConflictingOrderId()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "chooserfive");
        var alreadyClaimed = NewOrder("ALREADY-CLAIMED-2", DateTimeOffset.UtcNow);
        alreadyClaimed.Status = OrderStatus.InProgress;
        alreadyClaimed.ClaimedByEmployeeId = employee.Id;
        alreadyClaimed.ClaimedAt = DateTimeOffset.UtcNow;
        var target = NewOrder("TARGET-ORDER", DateTimeOffset.UtcNow);
        await factory.SeedAsync(context =>
        {
            context.Orders.AddRange(alreadyClaimed, target);
            return Task.CompletedTask;
        });

        var response = await client.PostAsync($"/api/orders/{target.Id}/claim", content: null);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "employee_has_active_claim",
            document.RootElement.GetProperty("error").GetString()
        );
        Assert.Equal(
            alreadyClaimed.Id,
            document.RootElement.GetProperty("claimedOrderId").GetInt32()
        );
    }

    [Fact]
    public async Task PickNext_WithoutSession_Returns401()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.EnsureDatabaseCreatedAsync();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsync("/api/orders/pick-next", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Claim_WithoutSession_Returns401()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.EnsureDatabaseCreatedAsync();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsync("/api/orders/1/claim", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<(HttpClient Client, Employee Employee)> LoginAsync(
        AuthWebApplicationFactory factory,
        string username
    )
    {
        var employee = new Employee
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = username,
            PinHash = new Pbkdf2PinHasher().Hash("1234"),
            Role = EmployeeRole.Picker,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await factory.SeedAsync(context =>
        {
            context.Employees.Add(employee);
            return Task.CompletedTask;
        });
        var client = factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(username, "1234")
        );
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (client, employee);
    }

    private static Order NewOrder(string tcgplayerOrderId, DateTimeOffset importedAt) =>
        new()
        {
            TcgplayerOrderId = tcgplayerOrderId,
            Status = OrderStatus.Ready,
            ImportedAt = importedAt,
        };
}
