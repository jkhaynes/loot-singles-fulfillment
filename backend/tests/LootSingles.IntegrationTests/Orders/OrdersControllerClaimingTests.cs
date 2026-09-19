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
    public async Task Release_ClaimantReleases_ReturnsOrderToReadyAndReturns200()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, _) = await LoginAsync(factory, "releaserone");
        var target = NewOrder("RELEASE-ORDER", DateTimeOffset.UtcNow);
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(target);
            return Task.CompletedTask;
        });
        var claim = await client.PostAsync($"/api/orders/{target.Id}/claim", content: null);
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);

        var response = await client.PostAsync($"/api/orders/{target.Id}/release", content: null);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ready", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            document.RootElement.GetProperty("claimedByEmployeeId").ValueKind
        );

        var reclaim = await client.PostAsync($"/api/orders/{target.Id}/claim", content: null);
        Assert.Equal(HttpStatusCode.OK, reclaim.StatusCode);
    }

    [Fact]
    public async Task Release_OrderDoesNotExist_Returns404()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, _) = await LoginAsync(factory, "releasertwo");

        var response = await client.PostAsync("/api/orders/2147483647/release", content: null);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("order_not_found", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Release_OrderNotClaimedByCaller_Returns409NotYourClaim()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, _) = await LoginAsync(factory, "releaserthree");
        var unclaimed = NewOrder("UNCLAIMED-RELEASE-ORDER", DateTimeOffset.UtcNow);
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(unclaimed);
            return Task.CompletedTask;
        });

        var response = await client.PostAsync($"/api/orders/{unclaimed.Id}/release", content: null);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("not_your_claim", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Release_OrderClaimedByAnotherEmployee_Returns409NotYourClaim()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (firstClient, _) = await LoginAsync(factory, "releaserfour");
        var (secondClient, _) = await LoginAsync(factory, "releaserfive");
        var target = NewOrder("OTHERS-CLAIM-ORDER", DateTimeOffset.UtcNow);
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(target);
            return Task.CompletedTask;
        });
        var claim = await firstClient.PostAsync($"/api/orders/{target.Id}/claim", content: null);
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);

        var response = await secondClient.PostAsync(
            $"/api/orders/{target.Id}/release",
            content: null
        );
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("not_your_claim", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Release_WithoutSession_Returns401()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.EnsureDatabaseCreatedAsync();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsync("/api/orders/1/release", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ForceRelease_ByManager_ReturnsOrderToReadyAndReturns200()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (pickerClient, _) = await LoginAsync(factory, "forcereleasepicker");
        var (managerClient, _) = await LoginAsync(
            factory,
            "forcereleasemanager",
            EmployeeRole.ManagerAdmin
        );
        var target = NewOrder("FORCE-RELEASE-ORDER", DateTimeOffset.UtcNow);
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(target);
            return Task.CompletedTask;
        });
        var claim = await pickerClient.PostAsync($"/api/orders/{target.Id}/claim", content: null);
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);

        var response = await managerClient.PostAsync(
            $"/api/orders/{target.Id}/force-release",
            content: null
        );
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ready", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            document.RootElement.GetProperty("claimedByEmployeeId").ValueKind
        );

        var reclaim = await pickerClient.PostAsync($"/api/orders/{target.Id}/claim", content: null);
        Assert.Equal(HttpStatusCode.OK, reclaim.StatusCode);
    }

    [Fact]
    public async Task ForceRelease_OrderDoesNotExist_Returns404()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (managerClient, _) = await LoginAsync(
            factory,
            "forcereleasemanagertwo",
            EmployeeRole.ManagerAdmin
        );

        var response = await managerClient.PostAsync(
            "/api/orders/2147483647/force-release",
            content: null
        );
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("order_not_found", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ForceRelease_OrderNotCurrentlyClaimed_Returns409OrderNotClaimed()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (managerClient, _) = await LoginAsync(
            factory,
            "forcereleasemanagerthree",
            EmployeeRole.ManagerAdmin
        );
        var unclaimed = NewOrder("UNCLAIMED-FORCE-RELEASE-ORDER", DateTimeOffset.UtcNow);
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(unclaimed);
            return Task.CompletedTask;
        });

        var response = await managerClient.PostAsync(
            $"/api/orders/{unclaimed.Id}/force-release",
            content: null
        );
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("order_not_claimed", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ForceRelease_ByNonManager_Returns403()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (pickerClient, _) = await LoginAsync(factory, "forcereleasenonmanager");
        var target = NewOrder("NON-MANAGER-TARGET-ORDER", DateTimeOffset.UtcNow);
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(target);
            return Task.CompletedTask;
        });
        var claim = await pickerClient.PostAsync($"/api/orders/{target.Id}/claim", content: null);
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);

        var response = await pickerClient.PostAsync(
            $"/api/orders/{target.Id}/force-release",
            content: null
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ForceRelease_WithoutSession_Returns401()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.EnsureDatabaseCreatedAsync();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsync("/api/orders/1/force-release", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

    // 015-pick-completion T011: release/force-release derive status from line outcomes instead of
    // hardcoding Ready (research.md §5, FR-005).
    [Fact]
    public async Task Release_OrderWithUnresolvedIssueLine_StaysNeedsAttention()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "issuereleaser");
        var target = ClaimedOrder("ISSUE-RELEASE-ORDER", employee, PickOutcome.HasIssue, null);
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(target);
            return Task.CompletedTask;
        });

        var response = await client.PostAsync($"/api/orders/{target.Id}/release", content: null);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("needsAttention", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            document.RootElement.GetProperty("claimedByEmployeeId").ValueKind
        );
    }

    [Fact]
    public async Task ForceRelease_OrderWithUnresolvedIssueLine_StaysNeedsAttention()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (_, picker) = await LoginAsync(factory, "issueforcepicker");
        var (managerClient, _) = await LoginAsync(
            factory,
            "issueforcemanager",
            EmployeeRole.ManagerAdmin
        );
        var target = ClaimedOrder("ISSUE-FORCE-ORDER", picker, PickOutcome.HasIssue, null);
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(target);
            return Task.CompletedTask;
        });

        var response = await managerClient.PostAsync(
            $"/api/orders/{target.Id}/force-release",
            content: null
        );
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("needsAttention", document.RootElement.GetProperty("status").GetString());
    }

    // Product Owner decision 2026-09-19: a Picked order keeps its claim, and releasing it leaves
    // it Picked — never back to Ready.
    [Fact]
    public async Task Release_FullyPickedOrder_StaysPicked()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "pickedreleaser");
        var target = ClaimedOrder(
            "PICKED-RELEASE-ORDER",
            employee,
            PickOutcome.Picked,
            PickOutcome.Picked
        );
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(target);
            return Task.CompletedTask;
        });

        var response = await client.PostAsync($"/api/orders/{target.Id}/release", content: null);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("picked", document.RootElement.GetProperty("status").GetString());
    }

    // 015-pick-completion T012: re-claiming via Choose Order keeps an unresolved issue visible,
    // and Pick Next never hands out a Needs Attention order (FR-005).
    [Fact]
    public async Task Claim_ReleasedOrderWithUnresolvedIssueLine_StaysNeedsAttention()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, _) = await LoginAsync(factory, "issuereclaimer");
        var target = UnclaimedNeedsAttentionOrder("ISSUE-RECLAIM-ORDER");
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(target);
            return Task.CompletedTask;
        });

        var response = await client.PostAsync($"/api/orders/{target.Id}/claim", content: null);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("needsAttention", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PickNext_ReadyAndNeedsAttentionOrders_ClaimsOnlyTheReadyOne()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, _) = await LoginAsync(factory, "picknextissue");
        var flagged = UnclaimedNeedsAttentionOrder("FLAGGED-OLDEST-ORDER");
        flagged.ImportedAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
        var ready = NewOrder("READY-NEWER-ORDER", DateTimeOffset.Parse("2026-08-02T00:00:00Z"));
        await factory.SeedAsync(context =>
        {
            context.Orders.AddRange(flagged, ready);
            return Task.CompletedTask;
        });

        var first = await client.PostAsync("/api/orders/pick-next", content: null);
        using var firstDocument = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(ready.Id, firstDocument.RootElement.GetProperty("orderId").GetInt32());

        // Only the flagged order is left unclaimed; a second picker's Pick Next must not take it.
        var (otherClient, _) = await LoginAsync(factory, "picknextissuetwo");
        var second = await otherClient.PostAsync("/api/orders/pick-next", content: null);
        using var secondDocument = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(
            "no_orders_available",
            secondDocument.RootElement.GetProperty("error").GetString()
        );
    }

    private static Order ClaimedOrder(
        string tcgplayerOrderId,
        Employee claimant,
        PickOutcome? firstLineOutcome,
        PickOutcome? secondLineOutcome
    )
    {
        var order = NewOrder(tcgplayerOrderId, DateTimeOffset.UtcNow);
        order.Status = OrderStatus.InProgress;
        order.ClaimedByEmployeeId = claimant.Id;
        order.ClaimedAt = DateTimeOffset.UtcNow;
        order.OrderLines.Add(NewLine("First Card", firstLineOutcome));
        order.OrderLines.Add(NewLine("Second Card", secondLineOutcome));
        return order;
    }

    private static Order UnclaimedNeedsAttentionOrder(string tcgplayerOrderId)
    {
        var order = NewOrder(tcgplayerOrderId, DateTimeOffset.UtcNow);
        order.Status = OrderStatus.NeedsAttention;
        order.OrderLines.Add(NewLine("Missing Card", PickOutcome.HasIssue));
        order.OrderLines.Add(NewLine("Found Card", PickOutcome.Picked));
        return order;
    }

    private static OrderLine NewLine(string productName, PickOutcome? outcome) =>
        new()
        {
            RawDescription = productName,
            ProductLine = "Magic",
            ProductName = productName,
            Set = "Alpha",
            CollectorNumber = "#1",
            Condition = "Near Mint",
            Quantity = 1,
            PickOutcome = outcome,
        };

    private static async Task<(HttpClient Client, Employee Employee)> LoginAsync(
        AuthWebApplicationFactory factory,
        string username,
        EmployeeRole role = EmployeeRole.Picker
    )
    {
        var employee = new Employee
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = username,
            PinHash = new Pbkdf2PinHasher().Hash("1234"),
            Role = role,
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
