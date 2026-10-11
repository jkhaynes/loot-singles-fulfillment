using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LootSingles.Application.Packing;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Auth;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace LootSingles.IntegrationTests.Persistence;

/// <summary>
/// 021 T014 / FR-016 / FR-018 / SC-009: a soft-deleted order (<c>DeletedAt</c> set) is hidden from
/// every reader and refused by every action, exactly as a missing order is, and its history is
/// left alone. Deletion code does not exist yet, so the rows are deleted with a raw UPDATE.
/// </summary>
public sealed class OrderSoftDeleteSchemaTests
{
    [Fact]
    public async Task Deleted_orders_are_absent_from_every_reader()
    {
        await using var factory = new AuthWebApplicationFactory();
        var world = await SeedWorldAsync(factory);

        // GET /api/orders: only the live control order.
        var list = await JsonAsync(await world.Picker.GetAsync("/api/orders"));
        Assert.Equal(
            [world.Live.TcgplayerOrderId],
            list.EnumerateArray().Select(o => o.GetProperty("tcgplayerOrderId").GetString()!)
        );

        foreach (var order in world.Deleted)
        {
            await AssertOrderNotFoundAsync(
                await world.Picker.GetAsync($"/api/orders/{order.Id}"),
                $"detail {order.TcgplayerOrderId}"
            );
            await AssertOrderNotFoundAsync(
                await world.Picker.GetAsync($"/api/orders/{order.Id}/label"),
                $"label {order.TcgplayerOrderId}"
            );

            // The packing desk lookup, by id and by TCGplayer order number.
            await AssertOrderNotFoundAsync(
                await world.Picker.GetAsync($"/api/packing/orders/{order.Id}"),
                $"desk by id {order.TcgplayerOrderId}"
            );
            await AssertOrderNotFoundAsync(
                await world.Picker.GetAsync($"/api/packing/orders/{order.TcgplayerOrderId}"),
                $"desk by number {order.TcgplayerOrderId}"
            );
        }

        // Every dashboard count. The live order is Ready; the picker holds a claim on a deleted
        // order, so the active claim is gone too.
        var dashboard = await JsonAsync(await world.Picker.GetAsync("/api/dashboard"));
        Assert.Equal(1, dashboard.GetProperty("ready").GetProperty("count").GetInt32());
        Assert.Equal(0, dashboard.GetProperty("inProgress").GetProperty("count").GetInt32());
        Assert.Equal(0, dashboard.GetProperty("needsAttention").GetProperty("count").GetInt32());
        Assert.Equal(0, dashboard.GetProperty("picked").GetProperty("count").GetInt32());
        Assert.Equal(JsonValueKind.Null, dashboard.GetProperty("activeClaim").ValueKind);

        // The awaiting-packing list, over HTTP and straight from the repository.
        var awaiting = await JsonAsync(await world.Picker.GetAsync("/api/packing/awaiting"));
        Assert.Empty(awaiting.EnumerateArray());
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPackingRepository>();
        Assert.Empty(await repository.GetAwaitingPackingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PickNext_skips_deleted_orders_even_when_they_are_the_oldest()
    {
        await using var factory = new AuthWebApplicationFactory();
        var world = await SeedWorldAsync(factory, pickerHoldsClaim: false);

        var response = await world.Picker.PostAsync("/api/orders/pick-next", content: null);

        var claimed = await JsonAsync(response);
        Assert.Equal(world.Live.Id, claimed.GetProperty("orderId").GetInt32());
    }

    [Fact]
    public async Task Every_action_on_a_deleted_order_is_refused_as_order_not_found()
    {
        await using var factory = new AuthWebApplicationFactory();
        var world = await SeedWorldAsync(factory);

        await AttemptEveryActionAsync(world);
    }

    [Fact]
    public async Task Refused_actions_leave_lines_outcomes_issues_and_claim_fields_unchanged()
    {
        await using var factory = new AuthWebApplicationFactory();
        var world = await SeedWorldAsync(factory);
        var before = await SnapshotAsync(factory, world.Deleted);

        await AttemptEveryActionAsync(world);

        var after = await SnapshotAsync(factory, world.Deleted);
        Assert.Equal(before, after);
        // Guard against a vacuous comparison: the snapshot really holds the history.
        Assert.Contains("issue", before[world.NeedsAttention.Id]);
        Assert.Contains("claimedBy=" + world.PickerId, before[world.Claimed.Id]);
    }

    [Fact]
    public async Task A_new_order_can_reuse_a_deleted_orders_TcgplayerOrderId_but_not_a_live_one()
    {
        await using var factory = new AuthWebApplicationFactory();
        var world = await SeedWorldAsync(factory);

        await factory.SeedAsync(context =>
        {
            context.Orders.Add(NewOrder(world.Ready.TcgplayerOrderId, OrderStatus.Ready));
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            factory.SeedAsync(context =>
            {
                context.Orders.Add(NewOrder(world.Live.TcgplayerOrderId, OrderStatus.Ready));
                return Task.CompletedTask;
            })
        );
    }

    [Fact]
    public async Task An_employee_holding_a_deleted_orders_claim_can_claim_another_order()
    {
        await using var factory = new AuthWebApplicationFactory();
        var world = await SeedWorldAsync(factory);

        var response = await world.Picker.PostAsync(
            $"/api/orders/{world.Live.Id}/claim",
            content: null
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Every order-scoped action, against every deleted order, as the right role.</summary>
    private static async Task AttemptEveryActionAsync(World world)
    {
        foreach (var order in world.Deleted)
        {
            var line = order.OrderLines.First();
            var attempts = new (string Name, Task<HttpResponseMessage> Response)[]
            {
                ("claim", world.Picker.PostAsync($"/api/orders/{order.Id}/claim", null)),
                ("release", world.Picker.PostAsync($"/api/orders/{order.Id}/release", null)),
                (
                    "force-release",
                    world.Manager.PostAsync($"/api/orders/{order.Id}/force-release", null)
                ),
                (
                    "pick",
                    world.Picker.PostAsync($"/api/orders/{order.Id}/lines/{line.Id}/pick", null)
                ),
                (
                    "report-issue",
                    world.Picker.PostAsJsonAsync(
                        $"/api/orders/{order.Id}/lines/{line.Id}/report-issue",
                        new { issueType = "cardNotFound" }
                    )
                ),
                ("packed", world.Picker.PostAsync($"/api/orders/{order.Id}/packed", null)),
                ("packing-slip", world.Picker.GetAsync($"/api/orders/{order.Id}/packing-slip")),
            };

            foreach (var (name, pending) in attempts)
            {
                await AssertOrderNotFoundAsync(await pending, $"{name} {order.TcgplayerOrderId}");
            }
        }

        // A slip is stored for the awaiting order, so its 404 is the order filter and not an
        // absent slip (that answers packing_slip_unavailable). No retrieval was recorded.
        using var scope = world.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        Assert.Equal(1, await context.OrderPackingSlips.CountAsync());
        Assert.Equal(0, await context.PackingSlipAccesses.CountAsync());
    }

    private static async Task AssertOrderNotFoundAsync(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound,
            $"{what}: {response.StatusCode} {body}"
        );
        Assert.True(body.Contains("\"order_not_found\""), $"{what}: {body}");
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<Dictionary<int, string>> SnapshotAsync(
        AuthWebApplicationFactory factory,
        IEnumerable<Order> orders
    )
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        var ids = orders.Select(o => o.Id).ToArray();
        var loaded = await context
            .Orders.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(o => o.OrderLines)
            .Where(o => ids.Contains(o.Id))
            .ToListAsync();
        var issues = await context.Set<PickingIssue>().AsNoTracking().ToListAsync();

        return loaded.ToDictionary(
            o => o.Id,
            o =>
                $"status={o.Status} claimedBy={o.ClaimedByEmployeeId} claimedAt={o.ClaimedAt:O} "
                + $"first={o.FirstClaimedAt:O} packedBy={o.PackedByEmployeeId} packedAt={o.PackedAt:O} "
                + string.Join(
                    " | ",
                    o.OrderLines.OrderBy(l => l.Id)
                        .Select(l =>
                            $"line{l.Id} q={l.Quantity} outcome={l.PickOutcome} by={l.PickOutcomeRecordedByEmployeeId} "
                            + $"at={l.PickOutcomeRecordedAt:O} current={l.CurrentPickingIssueId} "
                            + string.Join(
                                ",",
                                issues
                                    .Where(i => i.OrderLineId == l.Id)
                                    .OrderBy(i => i.Id)
                                    .Select(i => $"issue{i.Id}:{i.IssueType}:{i.ReportedAt:O}")
                            )
                        )
                )
        );
    }

    private sealed record World(
        AuthWebApplicationFactory Factory,
        HttpClient Picker,
        HttpClient Manager,
        int PickerId,
        Order Live,
        Order Ready,
        Order Claimed,
        Order NeedsAttention,
        Order Awaiting
    )
    {
        public IReadOnlyList<Order> Deleted => [Ready, Claimed, NeedsAttention, Awaiting];
    }

    /// <summary>
    /// One live Ready order (newest) and four deleted ones covering every dashboard section: Ready,
    /// In Progress and claimed by the picker, Needs Attention with an issue, and Picked and awaiting
    /// packing with a stored slip. The deleted orders are older than the live one, so Pick Next
    /// would choose one of them if it could see them.
    /// </summary>
    private static async Task<World> SeedWorldAsync(
        AuthWebApplicationFactory factory,
        bool pickerHoldsClaim = true
    )
    {
        var picker = await AddEmployeeAsync(factory, "softdelpicker", EmployeeRole.Picker);
        var manager = await AddEmployeeAsync(factory, "softdelmanager", EmployeeRole.ManagerAdmin);
        var recorded = DateTimeOffset.Parse("2026-09-21T09:00:00Z");

        var live = NewOrder("SOFTDEL-LIVE", OrderStatus.Ready, "2026-09-22T08:00:00Z");
        live.OrderLines.Add(Line("Live Card"));

        var ready = NewOrder("SOFTDEL-READY", OrderStatus.Ready, "2026-09-21T08:00:00Z");
        ready.OrderLines.Add(Line("Ready Card"));

        var claimed = NewOrder("SOFTDEL-CLAIMED", OrderStatus.InProgress, "2026-09-21T08:01:00Z");
        claimed.ClaimedByEmployeeId = pickerHoldsClaim ? picker.Id : null;
        claimed.ClaimedAt = pickerHoldsClaim ? recorded : null;
        claimed.FirstClaimedAt = recorded;
        claimed.OrderLines.Add(Line("Claimed Card", PickOutcome.Picked, picker.Id, recorded));
        claimed.OrderLines.Add(Line("Claimed Card Two"));

        var needsAttention = NewOrder(
            "SOFTDEL-ISSUE",
            OrderStatus.NeedsAttention,
            "2026-09-21T08:02:00Z"
        );
        var issueLine = Line("Issue Card", PickOutcome.HasIssue, picker.Id, recorded);
        needsAttention.OrderLines.Add(issueLine);

        var awaiting = NewOrder("SOFTDEL-PICKED", OrderStatus.Picked, "2026-09-21T08:03:00Z");
        awaiting.OrderLines.Add(Line("Picked Card", PickOutcome.Picked, picker.Id, recorded));

        await factory.SeedAsync(context =>
        {
            context.Orders.AddRange(live, ready, claimed, needsAttention, awaiting);
            return Task.CompletedTask;
        });
        await factory.SeedAsync(context =>
        {
            context.Add(
                new PickingIssue
                {
                    OrderLineId = issueLine.Id,
                    IssueType = PickingIssueType.CardNotFound,
                    ReportedByEmployeeId = picker.Id,
                    ReportedAt = recorded,
                    Note = "not on the shelf",
                }
            );
            context.OrderPackingSlips.Add(
                new OrderPackingSlip
                {
                    OrderId = awaiting.Id,
                    Content = [1, 2, 3],
                    StoredAt = recorded,
                }
            );
            return Task.CompletedTask;
        });
        await factory.SeedAsync(async context =>
        {
            var line = await context.OrderLines.SingleAsync(l => l.Id == issueLine.Id);
            line.CurrentPickingIssueId = await context
                .Set<PickingIssue>()
                .Where(i => i.OrderLineId == issueLine.Id)
                .Select(i => i.Id)
                .SingleAsync();
        });

        // The deletion under test: a raw UPDATE, as deletion code does not exist yet.
        var ids = new[] { ready.Id, claimed.Id, needsAttention.Id, awaiting.Id };
        await factory.SeedAsync(async context =>
        {
            foreach (var id in ids)
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE Orders SET DeletedAt = SYSDATETIMEOFFSET(), DeletedByEmployeeId = {manager.Id} WHERE Id = {id}"
                );
            }
        });

        return new World(
            factory,
            await LoginAsync(factory, picker.Username),
            await LoginAsync(factory, manager.Username),
            picker.Id,
            live,
            ready,
            claimed,
            needsAttention,
            awaiting
        );
    }

    private static async Task<Employee> AddEmployeeAsync(
        AuthWebApplicationFactory factory,
        string username,
        EmployeeRole role
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
        return employee;
    }

    private static async Task<HttpClient> LoginAsync(
        AuthWebApplicationFactory factory,
        string username
    )
    {
        var client = factory.CreateAuthenticatedClient();
        (
            await client.PostAsJsonAsync("/api/auth/login", new { username, pin = "1234" })
        ).EnsureSuccessStatusCode();
        return client;
    }

    internal static Order NewOrder(
        string tcgplayerOrderId,
        OrderStatus status,
        string importedAt = "2026-09-21T08:00:00Z"
    ) =>
        new()
        {
            TcgplayerOrderId = tcgplayerOrderId,
            Status = status,
            ImportedAt = DateTimeOffset.Parse(importedAt),
        };

    internal static OrderLine Line(
        string productName,
        PickOutcome? outcome = null,
        int? employeeId = null,
        DateTimeOffset? recordedAt = null
    ) =>
        new()
        {
            RawDescription = $"{productName} - 001/100 - Near Mint",
            ProductLine = "Pokemon",
            ProductName = productName,
            Set = "Base Set",
            CollectorNumber = "001/100",
            Condition = "Near Mint",
            Quantity = 1,
            PickOutcome = outcome,
            PickOutcomeRecordedByEmployeeId = employeeId,
            PickOutcomeRecordedAt = recordedAt,
        };
}

/// <summary>
/// 021 data-model.md "Backfill": the migration stamps <c>FirstClaimedAt</c> on orders that were
/// already touched, using the earliest evidence, and leaves untouched orders null. The database is
/// migrated back to the previous migration, then forward again.
/// </summary>
[Collection(SqlServerTestCollection.Name)]
public sealed class OrderSoftDeleteBackfillTests(SqlServerContainerFixture fixture)
{
    private const string PreviousMigration = "20261010023033_AddTcgplayerApiImport";

    [Fact]
    public async Task Migration_backfills_FirstClaimedAt_on_touched_orders_and_leaves_untouched_ones_null()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var context = lease.CreateDbContext();
        var migrator = context.GetService<IMigrator>();

        var early = DateTimeOffset.Parse("2026-09-20T10:00:00Z");
        var middle = early.AddHours(1);
        var late = early.AddHours(2);

        var employee = new Employee
        {
            Username = "backfill",
            NormalizedUsername = "BACKFILL",
            DisplayName = "backfill",
            PinHash = new Pbkdf2PinHasher().Hash("1234"),
            Role = EmployeeRole.Picker,
            CreatedAt = early,
        };
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var untouched = OrderSoftDeleteSchemaTests.NewOrder("BF-UNTOUCHED", OrderStatus.Ready);
        untouched.OrderLines.Add(OrderSoftDeleteSchemaTests.Line("Untouched"));

        var outcomeOnly = OrderSoftDeleteSchemaTests.NewOrder("BF-OUTCOME", OrderStatus.InProgress);
        outcomeOnly.OrderLines.Add(
            OrderSoftDeleteSchemaTests.Line("Outcome", PickOutcome.Picked, employee.Id, middle)
        );
        outcomeOnly.OrderLines.Add(
            OrderSoftDeleteSchemaTests.Line("Outcome Later", PickOutcome.Picked, employee.Id, late)
        );

        var issueFirst = OrderSoftDeleteSchemaTests.NewOrder(
            "BF-ISSUE",
            OrderStatus.NeedsAttention
        );
        var issueLine = OrderSoftDeleteSchemaTests.Line(
            "Issue",
            PickOutcome.HasIssue,
            employee.Id,
            middle
        );
        issueFirst.OrderLines.Add(issueLine);

        var packedOnly = OrderSoftDeleteSchemaTests.NewOrder("BF-PACKED", OrderStatus.Packed);
        packedOnly.PackedAt = late;
        packedOnly.PackedByEmployeeId = employee.Id;
        packedOnly.OrderLines.Add(OrderSoftDeleteSchemaTests.Line("Packed"));

        var claimed = OrderSoftDeleteSchemaTests.NewOrder("BF-CLAIMED", OrderStatus.InProgress);
        claimed.ClaimedByEmployeeId = employee.Id;
        claimed.ClaimedAt = early;
        claimed.OrderLines.Add(OrderSoftDeleteSchemaTests.Line("Claimed"));

        context.Orders.AddRange(untouched, outcomeOnly, issueFirst, packedOnly, claimed);
        await context.SaveChangesAsync();

        context.Add(
            new PickingIssue
            {
                OrderLineId = issueLine.Id,
                IssueType = PickingIssueType.CardNotFound,
                ReportedByEmployeeId = employee.Id,
                ReportedAt = early,
            }
        );
        await context.SaveChangesAsync();

        // Back to before this feature's migration: the new columns are dropped, which is the shape
        // real rows had when they were written.
        await migrator.MigrateAsync(PreviousMigration);
        Assert.DoesNotContain("FirstClaimedAt", await ColumnNamesAsync(context));

        await migrator.MigrateAsync();

        var stamps = await context
            .Orders.IgnoreQueryFilters()
            .AsNoTracking()
            .ToDictionaryAsync(o => o.TcgplayerOrderId, o => o.FirstClaimedAt);

        Assert.Null(stamps["BF-UNTOUCHED"]);
        Assert.Equal(middle, stamps["BF-OUTCOME"]);
        Assert.Equal(early, stamps["BF-ISSUE"]);
        Assert.Equal(late, stamps["BF-PACKED"]);
        Assert.Equal(early, stamps["BF-CLAIMED"]);
    }

    private static async Task<List<string>> ColumnNamesAsync(LootSinglesDbContext context) =>
        await context
            .Database.SqlQuery<string>(
                $"SELECT COLUMN_NAME AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Orders'"
            )
            .ToListAsync();
}
