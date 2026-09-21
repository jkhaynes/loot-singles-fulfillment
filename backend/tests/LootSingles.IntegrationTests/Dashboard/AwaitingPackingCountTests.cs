using System.Net.Http.Json;
using System.Text.Json;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.IntegrationTests.Auth;

namespace LootSingles.IntegrationTests.Dashboard;

/// <summary>
/// 017-pick-completion-handoff T065 / FR-036 — the dashboard counts what is still on the shelf.
///
/// <para>
/// This passed the moment it was written, and that is the point worth recording rather than
/// hiding: the count filters on <see cref="OrderStatus.Picked"/>, and Phase 2's short-circuit
/// means a packed order's status <em>is</em> <see cref="OrderStatus.Packed"/>. The behaviour came
/// free from that design. The test exists so a later change to the derivation cannot quietly turn
/// the tile back into a number that only ever grows.
/// </para>
/// </summary>
public sealed class AwaitingPackingCountTests
{
    [Fact]
    public async Task PackingAnOrder_RemovesItFromTheAwaitingPackingCount()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "awaitingcount");
        var order = NewPickedOrder("AWAITING-COUNT", employee.Id);
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(order);
            return Task.CompletedTask;
        });

        var before = await CountAsync(client);
        (await client.PostAsync($"/api/orders/{order.Id}/packed", null)).EnsureSuccessStatusCode();
        var after = await CountAsync(client);

        Assert.Equal(1, before);
        // The old behaviour — a picked count that only ever grew — is the thing being corrected.
        Assert.Equal(0, after);
    }

    private static async Task<int> CountAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/dashboard");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("picked").GetProperty("count").GetInt32();
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
        (
            await client.PostAsJsonAsync("/api/auth/login", new { username, pin = "1234" })
        ).EnsureSuccessStatusCode();
        return (client, employee);
    }

    private static Order NewPickedOrder(string tcgplayerOrderId, int employeeId) =>
        new()
        {
            TcgplayerOrderId = tcgplayerOrderId,
            Status = OrderStatus.Picked,
            ImportedAt = DateTimeOffset.Parse("2026-09-21T08:00:00Z"),
            OrderLines =
            [
                new OrderLine
                {
                    RawDescription = "Awaiting Card - 001/100 - Near Mint",
                    ProductLine = "Pokemon",
                    ProductName = "Awaiting Card",
                    Set = "Base Set",
                    CollectorNumber = "001/100",
                    Condition = "Near Mint",
                    Quantity = 1,
                    PickOutcome = PickOutcome.Picked,
                    PickOutcomeRecordedByEmployeeId = employeeId,
                    PickOutcomeRecordedAt = DateTimeOffset.Parse("2026-09-21T09:00:00Z"),
                },
            ],
        };
}
