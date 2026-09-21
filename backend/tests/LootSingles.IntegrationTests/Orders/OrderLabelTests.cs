using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.IntegrationTests.Auth;

namespace LootSingles.IntegrationTests.Orders;

/// <summary>
/// 017-pick-completion-handoff T016 — the label endpoint.
/// </summary>
public sealed class OrderLabelTests
{
    [Fact]
    public async Task Label_PickedOrder_ReturnsCountsAndContributors()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "labelpicker");
        var order = NewOrder("LABEL-COMPLETE");
        order.OrderLines.Add(Line(3, PickOutcome.Picked, employee.Id, At("09:00")));
        order.OrderLines.Add(Line(5, PickOutcome.Picked, employee.Id, At("09:05")));
        await SeedAsync(factory, order);

        var response = await client.GetAsync($"/api/orders/{order.Id}/label");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(order.Id, root.GetProperty("orderId").GetInt32());
        Assert.Equal("LABEL-COMPLETE", root.GetProperty("tcgplayerOrderId").GetString());
        Assert.Equal(8, root.GetProperty("cardCount").GetInt32());
        Assert.False(root.GetProperty("isHeld").GetBoolean());
        Assert.False(root.GetProperty("shipsShort").GetBoolean());

        var pickedBy = root.GetProperty("pickedBy").EnumerateArray().ToList();
        var person = Assert.Single(pickedBy);
        Assert.Equal(employee.DisplayName, person.GetProperty("displayName").GetString());
        Assert.Equal(At("09:05"), root.GetProperty("pickedAt").GetDateTimeOffset());
    }

    /// <summary>
    /// The case that a status-based guard would have broken: a held order is
    /// <see cref="OrderStatus.NeedsAttention"/>, never <see cref="OrderStatus.Picked"/>, and must
    /// still produce a hold label (FR-009, FR-013).
    /// </summary>
    [Fact]
    public async Task Label_HeldOrder_ReturnsAHoldLabelNamingTheUnresolvedProduct()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "labelhold");
        var order = NewOrder("LABEL-HELD");
        order.OrderLines.Add(Line(7, PickOutcome.Picked, employee.Id, At("09:00")));
        order.OrderLines.Add(
            Line(1, PickOutcome.HasIssue, employee.Id, At("09:02"), productName: "Latias ex")
        );
        await SeedAsync(factory, order);

        var response = await client.GetAsync($"/api/orders/{order.Id}/label");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(root.GetProperty("isHeld").GetBoolean());
        Assert.Equal(7, root.GetProperty("cardCount").GetInt32());
        Assert.Equal(
            ["Latias ex"],
            root.GetProperty("unresolvedProducts").EnumerateArray().Select(item => item.GetString())
        );
        // FR-046 — nothing records a set-aside card yet, so the label says nothing rather than
        // printing a zero that would read as "no cards set aside".
        Assert.Equal(JsonValueKind.Null, root.GetProperty("setAsideCount").ValueKind);
    }

    [Fact]
    public async Task Label_OrderPickedByTwoEmployees_NamesBothInContributionOrder()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, first) = await LoginAsync(factory, "labelfirst");
        var second = await SeedEmployeeAsync(factory, "labelsecond");
        var order = NewOrder("LABEL-TWO-PICKERS");
        order.OrderLines.Add(Line(1, PickOutcome.Picked, second.Id, At("09:10")));
        order.OrderLines.Add(Line(1, PickOutcome.Picked, first.Id, At("09:00")));
        await SeedAsync(factory, order);

        var response = await client.GetAsync($"/api/orders/{order.Id}/label");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(
            [first.DisplayName, second.DisplayName],
            document
                .RootElement.GetProperty("pickedBy")
                .EnumerateArray()
                .Select(person => person.GetProperty("displayName").GetString())
        );
    }

    [Fact]
    public async Task Label_OrderNobodyHasTouched_Returns409()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, _) = await LoginAsync(factory, "labelunstarted");
        var order = NewOrder("LABEL-UNSTARTED");
        order.OrderLines.Add(Line(1, pickOutcome: null, employeeId: null, at: null));
        await SeedAsync(factory, order);

        var response = await client.GetAsync($"/api/orders/{order.Id}/label");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("order_not_started", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Label_UnknownOrder_Returns404()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, _) = await LoginAsync(factory, "labelmissing");

        var response = await client.GetAsync("/api/orders/999999/label");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("order_not_found", document.RootElement.GetProperty("error").GetString());
    }

    /// <summary>
    /// FR-015 — a dropped label must leak nothing. Asserted against the whole payload rather than
    /// a field list, so adding a customer field later fails here instead of reaching a printer.
    /// </summary>
    [Fact]
    public async Task Label_CarriesNoCustomerInformation()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "labelprivacy");
        var order = NewOrder("LABEL-PRIVACY");
        order.OrderLines.Add(Line(1, PickOutcome.Picked, employee.Id, At("09:00")));
        await SeedAsync(factory, order);

        var response = await client.GetAsync($"/api/orders/{order.Id}/label");
        var body = await response.Content.ReadAsStringAsync();

        foreach (
            var forbidden in new[]
            {
                "address",
                "customer",
                "buyer",
                "shipTo",
                "recipient",
                "phone",
            }
        )
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- helpers -------------------------------------------------------------------------

    private static DateTimeOffset At(string time) => DateTimeOffset.Parse($"2026-09-21T{time}:00Z");

    private static Task SeedAsync(AuthWebApplicationFactory factory, Order order) =>
        factory.SeedAsync(context =>
        {
            context.Orders.Add(order);
            return Task.CompletedTask;
        });

    private static async Task<Employee> SeedEmployeeAsync(
        AuthWebApplicationFactory factory,
        string username
    )
    {
        var employee = NewEmployee(username, EmployeeRole.Picker);
        await factory.SeedAsync(context =>
        {
            context.Employees.Add(employee);
            return Task.CompletedTask;
        });
        return employee;
    }

    private static async Task<(HttpClient Client, Employee Employee)> LoginAsync(
        AuthWebApplicationFactory factory,
        string username
    )
    {
        var employee = await SeedEmployeeAsync(factory, username);
        var client = factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username, pin = "1234" });
        login.EnsureSuccessStatusCode();
        return (client, employee);
    }

    private static Employee NewEmployee(string username, EmployeeRole role) =>
        new()
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = username,
            PinHash = new Pbkdf2PinHasher().Hash("1234"),
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static Order NewOrder(string tcgplayerOrderId) =>
        new()
        {
            TcgplayerOrderId = tcgplayerOrderId,
            Status = OrderStatus.Ready,
            ImportedAt = At("08:00"),
        };

    private static OrderLine Line(
        int quantity,
        PickOutcome? pickOutcome,
        int? employeeId,
        DateTimeOffset? at,
        string productName = "Charizard ex"
    ) =>
        new()
        {
            RawDescription = $"{productName} - 234/197 - Near Mint",
            ProductLine = "Pokemon",
            ProductName = productName,
            Set = "Obsidian Flames",
            CollectorNumber = "234/197",
            Condition = "Near Mint",
            Quantity = quantity,
            PickOutcome = pickOutcome,
            PickOutcomeRecordedByEmployeeId = employeeId,
            PickOutcomeRecordedAt = at,
        };
}
