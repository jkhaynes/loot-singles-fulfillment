using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LootSingles.Application.Packing;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Packing;

/// <summary>
/// 017-pick-completion-handoff T036–T040, T083 — the packing desk.
/// </summary>
public sealed class PackingDeskTests
{
    [Fact]
    public async Task Resolve_ByOrderNumber_ReturnsTheOrdersCountsAndPicker()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "deskresolve");
        var order = await SeedPickedOrderAsync(factory, "DESK-RESOLVE", employee.Id, cards: 8);

        var response = await client.GetAsync($"/api/packing/orders/{order.Id}");
        var root = await JsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(order.Id, root.GetProperty("orderId").GetInt32());
        Assert.Equal(8, root.GetProperty("cardCount").GetInt32());
        Assert.True(root.GetProperty("canPack").GetBoolean());
        Assert.Equal(
            employee.DisplayName,
            root.GetProperty("pickedBy")[0].GetProperty("displayName").GetString()
        );
    }

    [Fact]
    public async Task Resolve_ByScannedLinkOrIdentifier_ReachesTheSameOrder()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "deskshapes");
        var order = await SeedPickedOrderAsync(factory, "DESK-SHAPES", employee.Id, cards: 2);

        // A scanner is a keyboard: these are the three things that can arrive (FR-024).
        foreach (
            var code in new[]
            {
                $"{order.Id}",
                Uri.EscapeDataString($"https://loot.example/packing/{order.Id}"),
                "desk-shapes",
            }
        )
        {
            var response = await client.GetAsync($"/api/packing/orders/{code}");
            var root = await JsonAsync(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(order.Id, root.GetProperty("orderId").GetInt32());
        }
    }

    [Fact]
    public async Task Resolve_UnknownCode_SaysSoPlainly()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, _) = await LoginAsync(factory, "deskunknown");

        var response = await client.GetAsync("/api/packing/orders/NOT-A-REAL-ORDER");
        var root = await JsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("order_not_found", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Resolve_HeldOrder_RefusesAndNamesTheUnresolvedProduct()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "deskheld");
        var order = await SeedHeldOrderAsync(factory, "DESK-HELD", employee.Id);

        var response = await client.GetAsync($"/api/packing/orders/{order.Id}");
        var root = await JsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(root.GetProperty("canPack").GetBoolean());
        // FR-028 — "this order has a problem" leaves the packer unable to tell whether it is
        // theirs to fix. Naming the product answers that.
        Assert.Equal(
            ["Hold Me"],
            root.GetProperty("unresolvedProducts").EnumerateArray().Select(item => item.GetString())
        );
    }

    // Branch review round 4 (T109). The label calls an order held when any product is not picked,
    // which is right for a picker's ending but wrong at the desk: an order still being picked is
    // not a manager's problem. Round 3 nearly shipped "a manager still has to decide" here, with
    // the untouched product listed beneath it.
    [Fact]
    public async Task Resolve_OrderStillBeingPicked_SaysSoAndNamesNothing()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "deskpicking");
        var order = NewOrder("DESK-PICKING", OrderStatus.InProgress);
        order.OrderLines.Add(Line(1, PickOutcome.Picked, employee.Id, "Pulled So Far"));
        var untouched = Line(1, PickOutcome.Picked, employee.Id, "Not Yet Looked At");
        untouched.PickOutcome = null;
        untouched.PickOutcomeRecordedByEmployeeId = null;
        untouched.PickOutcomeRecordedAt = null;
        order.OrderLines.Add(untouched);
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(order);
            return Task.CompletedTask;
        });

        var response = await client.GetAsync($"/api/packing/orders/{order.Id}");
        var root = await JsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(root.GetProperty("canPack").GetBoolean());
        Assert.Contains("not finished picking", root.GetProperty("blockedReason").GetString());
        Assert.Empty(root.GetProperty("unresolvedProducts").EnumerateArray());
    }

    [Fact]
    public async Task MarkPacked_AwaitingOrder_RecordsWhoAndWhen()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "deskpack");
        var order = await SeedPickedOrderAsync(factory, "DESK-PACK", employee.Id, cards: 3);

        var response = await client.PostAsync($"/api/orders/{order.Id}/packed", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var packed = await ReadOrderAsync(factory, order.Id);
        Assert.Equal(OrderStatus.Packed, packed.Status);
        Assert.Equal(employee.Id, packed.PackedByEmployeeId);
        Assert.NotNull(packed.PackedAt);
    }

    [Fact]
    public async Task MarkPacked_AlreadyPacked_RefusesWithoutRecordingItTwice()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "deskrepack");
        var order = await SeedPickedOrderAsync(factory, "DESK-REPACK", employee.Id, cards: 1);
        (await client.PostAsync($"/api/orders/{order.Id}/packed", null)).EnsureSuccessStatusCode();
        var first = await ReadOrderAsync(factory, order.Id);

        var response = await client.PostAsync($"/api/orders/{order.Id}/packed", content: null);
        var root = await JsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("order_already_packed", root.GetProperty("error").GetString());
        Assert.Equal(first.PackedAt, (await ReadOrderAsync(factory, order.Id)).PackedAt);
    }

    [Fact]
    public async Task MarkPacked_HeldOrder_IsRefused()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "deskholdpack");
        var order = await SeedHeldOrderAsync(factory, "DESK-HOLD-PACK", employee.Id);

        var response = await client.PostAsync($"/api/orders/{order.Id}/packed", content: null);
        var root = await JsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("order_has_unresolved_issue", root.GetProperty("error").GetString());
        Assert.Equal(OrderStatus.NeedsAttention, (await ReadOrderAsync(factory, order.Id)).Status);
    }

    /// <summary>
    /// FR-033. The enforcement point is the backend (Constitution VI), and a read-then-write check
    /// has a race window by construction — which is the defect the requirement names.
    /// </summary>
    [Fact]
    public async Task MarkPacked_TwoSimultaneousAttempts_RecordExactlyOnePack()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (firstClient, employee) = await LoginAsync(factory, "deskraceone");
        var (secondClient, _) = await LoginAsync(factory, "deskracetwo");
        var order = await SeedPickedOrderAsync(factory, "DESK-RACE", employee.Id, cards: 1);

        var responses = await Task.WhenAll(
            firstClient.PostAsync($"/api/orders/{order.Id}/packed", content: null),
            secondClient.PostAsync($"/api/orders/{order.Id}/packed", content: null)
        );

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(
            1,
            responses.Count(response => response.StatusCode == HttpStatusCode.Conflict)
        );
        Assert.NotNull((await ReadOrderAsync(factory, order.Id)).PackedAt);
    }

    [Fact]
    public async Task AwaitingPacking_ListsPickedOrdersAndDropsThemOncePacked()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "deskawaiting");
        var order = await SeedPickedOrderAsync(factory, "DESK-AWAITING", employee.Id, cards: 4);

        var before = await JsonAsync(await client.GetAsync("/api/packing/awaiting"));
        Assert.Contains(
            before.EnumerateArray(),
            item => item.GetProperty("orderId").GetInt32() == order.Id
        );

        (await client.PostAsync($"/api/orders/{order.Id}/packed", null)).EnsureSuccessStatusCode();

        var after = await JsonAsync(await client.GetAsync("/api/packing/awaiting"));
        Assert.DoesNotContain(
            after.EnumerateArray(),
            item => item.GetProperty("orderId").GetInt32() == order.Id
        );
    }

    [Fact]
    public async Task PackingSlip_OrderWithoutOne_SaysSoRatherThanFailing()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "deskslipmissing");
        var order = await SeedPickedOrderAsync(factory, "DESK-NO-SLIP", employee.Id, cards: 1);

        var response = await client.GetAsync($"/api/orders/{order.Id}/packing-slip");
        var root = await JsonAsync(response);

        // FR-022 — every order imported before this feature is in exactly this state, and it is
        // normal rather than an error. Distinct from order_not_found so the desk can say which.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("packing_slip_unavailable", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task PackingSlip_RecordsEveryRetrievalWithEmployeeAndTime()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "deskslipaccess");
        var order = await SeedPickedOrderAsync(factory, "DESK-SLIP", employee.Id, cards: 1);
        await AttachSlipAsync(factory, order.Id);

        var response = await client.GetAsync($"/api/orders/{order.Id}/packing-slip");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        // FR-038 / SC-010. Stdout logging is ephemeral on the hosted environment, so the durable
        // record is what makes this answerable after the fact.
        PackingSlipAccess? access = null;
        await factory.SeedAsync(async context =>
        {
            access = await context
                .PackingSlipAccesses.AsNoTracking()
                .SingleOrDefaultAsync(row => row.OrderId == order.Id);
        });
        Assert.NotNull(access);
        Assert.Equal(employee.Id, access.EmployeeId);
        Assert.NotEqual(default, access.RetrievedAt);
    }

    /// <summary>
    /// FR-037. There are only two roles and a packer signs in as a Picker, so a role check here
    /// would obstruct packing rather than protect anything. A requirement that something must NOT
    /// exist needs a test like any other — the codebase has RequireManagerAdmin close at hand.
    /// </summary>
    [Fact]
    public async Task APickerCanRetrieveASlipAndMarkAnOrderPacked()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "deskpicker", EmployeeRole.Picker);
        var order = await SeedPickedOrderAsync(factory, "DESK-PICKER-ROLE", employee.Id, cards: 1);
        await AttachSlipAsync(factory, order.Id);

        var slip = await client.GetAsync($"/api/orders/{order.Id}/packing-slip");
        var packed = await client.PostAsync($"/api/orders/{order.Id}/packed", content: null);

        Assert.Equal(HttpStatusCode.OK, slip.StatusCode);
        Assert.Equal(HttpStatusCode.OK, packed.StatusCode);
    }

    /// <summary>
    /// T087 / BR-001 — FR-034 under concurrency.
    ///
    /// <para>
    /// Feature 015 deliberately keeps a picked order re-claimable so a picker can revise a line
    /// before the sleeve is sealed. That makes "a picker reports an issue while a packer scans the
    /// same sleeve" a supported workflow rather than a contrived race, and the packing write must
    /// survive it.
    /// </para>
    ///
    /// <para>
    /// The reproduction is probabilistic. A deterministic one would need an interception seam
    /// between the desk's read and its write, which is not worth carrying in production code, so
    /// this races the two requests over enough fresh orders to land in the window reliably. It
    /// asserts the invariant rather than either outcome: whichever request wins is fine, but an
    /// order must never end up packed while one of its lines is unresolved.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PackingWhileAPickerReportsAnIssue_NeverPacksAnOrderWithAnUnresolvedLine()
    {
        const int attempts = 25;
        await using var factory = new AuthWebApplicationFactory();
        var (packerClient, _) = await LoginAsync(factory, "raceapacker");
        var (pickerClient, picker) = await LoginAsync(factory, "raceapicker");

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var order = await SeedPickedOrderAsync(
                factory,
                $"RACE-ISSUE-{attempt:D3}",
                picker.Id,
                cards: 1
            );

            // Re-claiming a picked order is what feature 015 allows, and it is what puts the
            // picker in a position to report an issue on an order already at the bench.
            (
                await pickerClient.PostAsync($"/api/orders/{order.Id}/claim", null)
            ).EnsureSuccessStatusCode();
            var lineId = await FirstLineIdAsync(factory, order.Id);

            await Task.WhenAll(
                packerClient.PostAsync($"/api/orders/{order.Id}/packed", content: null),
                pickerClient.PostAsJsonAsync(
                    $"/api/orders/{order.Id}/lines/{lineId}/report-issue",
                    new
                    {
                        issueType = nameof(PickingIssueType.CardNotFound),
                        requiredQuantity = (int?)null,
                        foundQuantity = (int?)null,
                        note = (string?)null,
                    }
                )
            );

            var packedWithUnresolvedLine = false;
            await factory.SeedAsync(async context =>
            {
                packedWithUnresolvedLine = await context
                    .Orders.AsNoTracking()
                    .AnyAsync(candidate =>
                        candidate.Id == order.Id
                        && candidate.PackedAt != null
                        && candidate.OrderLines.Any(line =>
                            line.PickOutcome == PickOutcome.HasIssue
                        )
                    );
            });

            Assert.False(
                packedWithUnresolvedLine,
                $"attempt {attempt}: order {order.Id} was packed while one of its lines was unresolved. "
                    + "Packed short-circuits the status derivation, so this never self-corrects (FR-034)."
            );

            // Release so the next iteration can claim; one claim per employee is enforced.
            await pickerClient.PostAsync($"/api/orders/{order.Id}/release", content: null);
        }
    }

    /// <summary>
    /// T090 / BR-002 — the awaiting list is bounded.
    /// <para>
    /// Batches of around 200 orders are documented as real, and a day's unpacked backlog is a
    /// list nobody reads to the end. The constitution requires limiting potentially large result
    /// sets; unbounded, this returns every awaiting order and every one of their lines.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AwaitingPacking_IsBounded_AndReturnsTheOldestFirst()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "deskbound");

        for (var index = 0; index < PackingLimits.AwaitingPageSize + 5; index++)
        {
            var order = NewOrder($"DESK-BOUND-{index:D3}", OrderStatus.Picked);
            order.ImportedAt = DateTimeOffset.Parse("2026-09-01T00:00:00Z").AddMinutes(index);
            order.OrderLines.Add(Line(1, PickOutcome.Picked, employee.Id, $"Bound {index}"));
            await factory.SeedAsync(context =>
            {
                context.Orders.Add(order);
                return Task.CompletedTask;
            });
        }

        var root = await JsonAsync(await client.GetAsync("/api/packing/awaiting"));
        var returned = root.EnumerateArray().ToList();

        Assert.Equal(PackingLimits.AwaitingPageSize, returned.Count);
        // Oldest first: the sleeve that has been on the shelf longest is the one to pack next.
        Assert.Equal("DESK-BOUND-000", returned[0].GetProperty("tcgplayerOrderId").GetString());
    }

    /// <summary>
    /// T092 / BR-002 — counts and contributors survive the projection.
    /// <para>
    /// Moving the card count and the contributor list into SQL is exactly the kind of change that
    /// can quietly return the wrong number: summing every line instead of the picked ones,
    /// counting lines rather than cards, or losing a second picker to a missing Distinct. A
    /// multi-line, multi-picker order is the shape that catches all three.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AwaitingPacking_CountsCardsNotLines_AndNamesEveryContributor()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, first) = await LoginAsync(factory, "deskprojectone");
        var (_, second) = await LoginAsync(factory, "deskprojecttwo");

        var order = NewOrder("DESK-PROJECTION", OrderStatus.Picked);
        order.OrderLines.Add(Line(3, PickOutcome.Picked, first.Id, "Projection First"));
        order.OrderLines.Add(Line(5, PickOutcome.Picked, second.Id, "Projection Second"));
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(order);
            return Task.CompletedTask;
        });

        var root = await JsonAsync(await client.GetAsync("/api/packing/awaiting"));
        var view = root.EnumerateArray()
            .Single(item => item.GetProperty("orderId").GetInt32() == order.Id);

        // Two product lines, eight physical cards.
        Assert.Equal(8, view.GetProperty("cardCount").GetInt32());
        Assert.Equal(
            [first.DisplayName, second.DisplayName],
            view.GetProperty("pickedBy")
                .EnumerateArray()
                .Select(person => person.GetProperty("displayName").GetString())
                .OrderBy(name => name)
        );
        Assert.True(view.GetProperty("canPack").GetBoolean());
    }

    // ---- helpers -------------------------------------------------------------------------

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

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

    private static async Task<Order> ReadOrderAsync(AuthWebApplicationFactory factory, int orderId)
    {
        Order? order = null;
        await factory.SeedAsync(async context =>
        {
            order = await context.Orders.AsNoTracking().SingleAsync(row => row.Id == orderId);
        });
        return order!;
    }

    private static Task AttachSlipAsync(AuthWebApplicationFactory factory, int orderId) =>
        factory.SeedAsync(context =>
        {
            context.OrderPackingSlips.Add(
                new OrderPackingSlip
                {
                    OrderId = orderId,
                    Content = [0x25, 0x50, 0x44, 0x46],
                    StoredAt = DateTimeOffset.UtcNow,
                }
            );
            return Task.CompletedTask;
        });

    private static async Task<Order> SeedPickedOrderAsync(
        AuthWebApplicationFactory factory,
        string tcgplayerOrderId,
        int employeeId,
        int cards
    )
    {
        var order = NewOrder(tcgplayerOrderId, OrderStatus.Picked);
        order.OrderLines.Add(Line(cards, PickOutcome.Picked, employeeId, "Packed Card"));
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(order);
            return Task.CompletedTask;
        });
        return order;
    }

    private static async Task<Order> SeedHeldOrderAsync(
        AuthWebApplicationFactory factory,
        string tcgplayerOrderId,
        int employeeId
    )
    {
        var order = NewOrder(tcgplayerOrderId, OrderStatus.NeedsAttention);
        order.OrderLines.Add(Line(1, PickOutcome.Picked, employeeId, "Pulled Fine"));
        order.OrderLines.Add(Line(1, PickOutcome.HasIssue, employeeId, "Hold Me"));
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(order);
            return Task.CompletedTask;
        });
        return order;
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
        (
            await client.PostAsJsonAsync("/api/auth/login", new { username, pin = "1234" })
        ).EnsureSuccessStatusCode();
        return (client, employee);
    }

    private static Order NewOrder(string tcgplayerOrderId, OrderStatus status) =>
        new()
        {
            TcgplayerOrderId = tcgplayerOrderId,
            Status = status,
            ImportedAt = DateTimeOffset.Parse("2026-09-21T08:00:00Z"),
        };

    private static OrderLine Line(
        int quantity,
        PickOutcome outcome,
        int employeeId,
        string productName
    ) =>
        new()
        {
            RawDescription = $"{productName} - 001/100 - Near Mint",
            ProductLine = "Pokemon",
            ProductName = productName,
            Set = "Base Set",
            CollectorNumber = "001/100",
            Condition = "Near Mint",
            Quantity = quantity,
            PickOutcome = outcome,
            PickOutcomeRecordedByEmployeeId = employeeId,
            PickOutcomeRecordedAt = DateTimeOffset.Parse("2026-09-21T09:00:00Z"),
        };
}
