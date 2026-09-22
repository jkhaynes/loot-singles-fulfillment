using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Orders;

/// <summary>
/// 017-pick-completion-handoff T012/T013 — the regression plan.md names.
///
/// <para>
/// Feature 015 established that an order's status is never an independently-tracked flag: every
/// write that can change it recomputes it from the order's current lines and claim state. This
/// feature adds <see cref="OrderStatus.Packed"/>, which cannot be derived that way — nothing about
/// an order's lines changes when its sleeve goes in the mail — so a recorded
/// <see cref="Order.PackedAt"/> short-circuits ahead of the derivation instead.
/// </para>
///
/// <para>
/// The failure mode this guards is silent. If any existing write path recomputes a packed order
/// back to <see cref="OrderStatus.Picked"/>, nothing errors — the order simply reappears as
/// awaiting packing, and a sleeve that has already been posted looks like it is still on the
/// shelf. So every path that touches the derivation gets its own test rather than one
/// representative case.
/// </para>
/// </summary>
public sealed class PackedOrderStatusTests
{
    /// <summary>
    /// FR-045. A packed sleeve has physically left, so claiming one is refused outright — unlike a
    /// <see cref="OrderStatus.Picked"/> order, which feature 015 deliberately keeps re-claimable so
    /// a picker can revise lines before it is sealed.
    /// </summary>
    [Fact]
    public async Task ClaimingAPackedOrder_IsRefusedAndLeavesItPacked()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, _) = await LoginAsync(factory, "packedclaim");
        var order = await SeedPackedOrderAsync(factory, "PACKED-CLAIM");

        var response = await client.PostAsync($"/api/orders/{order.Id}/claim", content: null);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("order_already_packed", document.RootElement.GetProperty("error").GetString());
        Assert.Equal(OrderStatus.Packed, await StatusOfAsync(factory, order.Id));
        Assert.Null(await ClaimantOfAsync(factory, order.Id));
    }

    /// <summary>
    /// The other side of FR-045's boundary: a picked-but-unpacked order stays claimable, because
    /// that is how a mis-pick gets corrected. Guarding "finished" rather than "packed" would break
    /// it, so the distinction is asserted rather than assumed.
    /// </summary>
    [Fact]
    public async Task ClaimingAPickedButUnpackedOrder_StillSucceeds()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "pickedclaim");
        var order = NewOrder("PICKED-STILL-CLAIMABLE");
        order.Status = OrderStatus.Picked;
        order.OrderLines.Add(NewLine(PickOutcome.Picked));
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(order);
            return Task.CompletedTask;
        });

        var response = await client.PostAsync($"/api/orders/{order.Id}/claim", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(employee.Id, await ClaimantOfAsync(factory, order.Id));
    }

    [Fact]
    public async Task ReleasingAPackedOrder_LeavesItPacked()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "packedrelease");
        var order = await SeedPackedOrderAsync(factory, "PACKED-RELEASE");
        await ClaimForAsync(factory, order.Id, employee.Id);

        var response = await client.PostAsync($"/api/orders/{order.Id}/release", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OrderStatus.Packed, await StatusOfAsync(factory, order.Id));
    }

    [Fact]
    public async Task ForceReleasingAPackedOrder_LeavesItPacked()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, _) = await LoginAsync(factory, "packedforce", EmployeeRole.ManagerAdmin);
        var holder = await SeedEmployeeAsync(factory, "packedholder");
        var order = await SeedPackedOrderAsync(factory, "PACKED-FORCE");
        await ClaimForAsync(factory, order.Id, holder.Id);

        var response = await client.PostAsync(
            $"/api/orders/{order.Id}/force-release",
            content: null
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OrderStatus.Packed, await StatusOfAsync(factory, order.Id));
    }

    /// <summary>
    /// BR-001. Recording an outcome on a packed order used to succeed, which is how an order
    /// could end up <see cref="OrderStatus.Packed"/> with a <see cref="PickOutcome.HasIssue"/>
    /// line: the pack was legitimate, and a picker still holding the claim reported an issue
    /// afterwards. Packed is terminal (FR-031), so the write is refused rather than absorbed.
    /// </summary>
    [Fact]
    public async Task RecordingALineOutcomeOnAPackedOrder_IsRefused()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "packedpick");
        var order = await SeedPackedOrderAsync(factory, "PACKED-PICK", lineUnpicked: true);
        await ClaimForAsync(factory, order.Id, employee.Id);
        var lineId = await FirstLineIdAsync(factory, order.Id);

        var response = await client.PostAsync(
            $"/api/orders/{order.Id}/lines/{lineId}/pick",
            content: null
        );
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        // Not not_your_claim: this employee does hold the claim, and saying otherwise would send
        // them looking for a problem that is not there.
        Assert.Equal("order_already_packed", document.RootElement.GetProperty("error").GetString());
        Assert.Equal(OrderStatus.Packed, await StatusOfAsync(factory, order.Id));
    }

    /// <summary>
    /// T013 — FR-034. The guard belongs on the write that records the pack, but no such endpoint
    /// exists until User Story 2, so this asserts the derivation's half of it: an order carrying an
    /// unresolved issue derives to <see cref="OrderStatus.NeedsAttention"/> and never to
    /// <see cref="OrderStatus.Packed"/> merely because the derivation ran.
    /// </summary>
    [Fact]
    public async Task AnOrderWithAnUnresolvedIssue_DerivesToNeedsAttentionNotPacked()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "heldorder");
        var order = NewOrder("HELD-NOT-PACKED");
        order.OrderLines.Add(NewLine(pickOutcome: null));
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(order);
            return Task.CompletedTask;
        });
        await ClaimForAsync(factory, order.Id, employee.Id);
        var lineId = await FirstLineIdAsync(factory, order.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/orders/{order.Id}/lines/{lineId}/report-issue",
            new
            {
                issueType = nameof(PickingIssueType.CardNotFound),
                requiredQuantity = (int?)null,
                foundQuantity = (int?)null,
                note = (string?)null,
            }
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await StatusOfAsync(factory, order.Id);
        Assert.Equal(OrderStatus.NeedsAttention, status);
        Assert.NotEqual(OrderStatus.Packed, status);
    }

    // ---- helpers -------------------------------------------------------------------------

    /// <summary>
    /// Seeds an order already recorded as packed. The pack is written directly because the
    /// endpoint that records one arrives with User Story 2 — this test is about what the
    /// <em>existing</em> write paths do to an order in that state.
    /// </summary>
    private static async Task<Order> SeedPackedOrderAsync(
        AuthWebApplicationFactory factory,
        string tcgplayerOrderId,
        bool lineUnpicked = false
    )
    {
        var packer = await SeedEmployeeAsync(
            factory,
            $"packer-{tcgplayerOrderId}".ToLowerInvariant()
        );
        var order = NewOrder(tcgplayerOrderId);
        order.Status = OrderStatus.Packed;
        order.PackedAt = DateTimeOffset.UtcNow;
        order.PackedByEmployeeId = packer.Id;
        order.OrderLines.Add(NewLine(lineUnpicked ? null : PickOutcome.Picked));

        await factory.SeedAsync(context =>
        {
            context.Orders.Add(order);
            return Task.CompletedTask;
        });

        return order;
    }

    private static async Task<Employee> SeedEmployeeAsync(
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
        return employee;
    }

    private static Task ClaimForAsync(
        AuthWebApplicationFactory factory,
        int orderId,
        int employeeId
    ) =>
        factory.SeedAsync(async context =>
        {
            var order = await context.Orders.SingleAsync(candidate => candidate.Id == orderId);
            order.ClaimedByEmployeeId = employeeId;
            order.ClaimedAt = DateTimeOffset.UtcNow;
        });

    private static async Task<OrderStatus> StatusOfAsync(
        AuthWebApplicationFactory factory,
        int orderId
    )
    {
        var status = OrderStatus.Ready;
        await factory.SeedAsync(async context =>
        {
            status = await context
                .Orders.AsNoTracking()
                .Where(order => order.Id == orderId)
                .Select(order => order.Status)
                .SingleAsync();
        });
        return status;
    }

    private static async Task<int?> ClaimantOfAsync(AuthWebApplicationFactory factory, int orderId)
    {
        int? claimant = null;
        await factory.SeedAsync(async context =>
        {
            claimant = await context
                .Orders.AsNoTracking()
                .Where(order => order.Id == orderId)
                .Select(order => order.ClaimedByEmployeeId)
                .SingleAsync();
        });
        return claimant;
    }

    private static async Task<int> FirstLineIdAsync(AuthWebApplicationFactory factory, int orderId)
    {
        var lineId = 0;
        await factory.SeedAsync(async context =>
        {
            lineId = await context
                .OrderLines.AsNoTracking()
                .Where(line => line.OrderId == orderId)
                .Select(line => line.Id)
                .FirstAsync();
        });
        return lineId;
    }

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
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username, pin = "1234" });
        login.EnsureSuccessStatusCode();
        return (client, employee);
    }

    private static Order NewOrder(string tcgplayerOrderId) =>
        new()
        {
            TcgplayerOrderId = tcgplayerOrderId,
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.Parse("2026-09-01T12:00:00Z"),
        };

    private static OrderLine NewLine(PickOutcome? pickOutcome) =>
        new()
        {
            RawDescription = "Charizard ex - 234/197 - Near Mint Holofoil",
            ProductLine = "Pokemon",
            ProductName = "Charizard ex",
            Set = "Obsidian Flames",
            CollectorNumber = "234/197",
            Condition = "Near Mint",
            Quantity = 1,
            PickOutcome = pickOutcome,
        };
}
