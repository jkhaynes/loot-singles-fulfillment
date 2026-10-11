using LootSingles.Application.Orders;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace LootSingles.IntegrationTests.Orders;

/// <summary>
/// 021 T017: the SQL repository behind order deletion. The conditional soft delete (research R7),
/// the production rule (FR-013, FR-020), the deletion record (FR-015), the packing slip and its
/// retrieval log (FR-016), delete-all and the summary counts (contracts/api.md).
/// </summary>
[Collection(SqlServerTestCollection.Name)]
public sealed class OrderDeletionRepositoryTests(
    SqlServerContainerFixture fixture,
    ITestOutputHelper output
)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse(
        "2026-10-11T12:34:56.1234567+00:00"
    );
    private static readonly DateTimeOffset Recorded = DateTimeOffset.Parse("2026-10-01T09:00:00Z");

    [Fact]
    public async Task Production_rule_deletes_an_untouched_order()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        var world = await SeedWorldAsync(lease);

        var result = await NewService(lease, allowTouched: false)
            .DeleteAsync(world.Untouched.Id, world.Manager.Id, CancellationToken.None);

        Assert.Equal(OrderDeletionResult.Deleted, result);
        var row = await RowAsync(lease, world.Untouched.Id);
        Assert.Equal(Now, row.DeletedAt);
        Assert.Equal(world.Manager.Id, row.DeletedByEmployeeId);
    }

    [Fact]
    public async Task Production_rule_refuses_every_touched_order_and_leaves_its_row_unchanged()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        var world = await SeedWorldAsync(lease);
        var service = NewService(lease, allowTouched: false);

        foreach (var order in world.Touched)
        {
            var before = await RowAsync(lease, order.Id);

            var result = await service.DeleteAsync(
                order.Id,
                world.Manager.Id,
                CancellationToken.None
            );

            Assert.True(
                result == OrderDeletionResult.ClaimedBefore,
                $"{order.TcgplayerOrderId}: expected ClaimedBefore, got {result.Outcome}"
            );
            Assert.Equal(before, await RowAsync(lease, order.Id));
            Assert.Null(before.DeletedAt);
        }

        // The packed order's slip is still stored after the refusal.
        Assert.True(await SlipExistsAsync(lease, world.Packed.Id));
    }

    [Fact]
    public async Task Stage_rule_deletes_every_order_and_keeps_the_claim_fields()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        var world = await SeedWorldAsync(lease);
        var service = NewService(lease, allowTouched: true);

        foreach (var order in world.All)
        {
            var before = await RowAsync(lease, order.Id);

            var result = await service.DeleteAsync(
                order.Id,
                world.Manager.Id,
                CancellationToken.None
            );

            Assert.True(
                result == OrderDeletionResult.Deleted,
                $"{order.TcgplayerOrderId}: expected Deleted, got {result.Outcome}"
            );
            var after = await RowAsync(lease, order.Id);
            Assert.Equal(Now, after.DeletedAt);
            Assert.Equal(world.Manager.Id, after.DeletedByEmployeeId);
            // History is kept (FR-016): only the deletion record changed.
            Assert.Equal(
                before with
                {
                    DeletedAt = Now,
                    DeletedByEmployeeId = world.Manager.Id,
                },
                after
            );
        }

        var claimed = await RowAsync(lease, world.Claimed.Id);
        Assert.Equal(world.Picker.Id, claimed.ClaimedByEmployeeId);
        Assert.NotNull(claimed.ClaimedAt);
    }

    [Fact]
    public async Task Deleting_removes_the_packing_slip_and_keeps_the_retrieval_log()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        var world = await SeedWorldAsync(lease);

        await NewService(lease, allowTouched: false)
            .DeleteAsync(world.Untouched.Id, world.Manager.Id, CancellationToken.None);
        await NewService(lease, allowTouched: true)
            .DeleteAsync(world.Packed.Id, world.Manager.Id, CancellationToken.None);

        Assert.False(await SlipExistsAsync(lease, world.Untouched.Id));
        Assert.False(await SlipExistsAsync(lease, world.Packed.Id));
        Assert.Equal(2, await AccessCountAsync(lease, world.Packed.Id));
        // A slip of an order nobody deleted is untouched.
        Assert.True(await SlipExistsAsync(lease, world.Late.Id));
    }

    [Fact]
    public async Task Deleting_a_deleted_order_is_AlreadyDeleted_and_an_unknown_id_is_NotFound()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        var world = await SeedWorldAsync(lease);
        var service = NewService(lease, allowTouched: true);
        await service.DeleteAsync(world.Untouched.Id, world.Manager.Id, CancellationToken.None);
        var before = await RowAsync(lease, world.Untouched.Id);

        var again = await service.DeleteAsync(
            world.Untouched.Id,
            world.OtherManager.Id,
            CancellationToken.None
        );
        var unknown = await service.DeleteAsync(999_999, world.Manager.Id, CancellationToken.None);

        Assert.Equal(OrderDeletionResult.AlreadyDeleted, again);
        Assert.Equal(OrderDeletionResult.NotFound, unknown);
        // The first deletion's record stands.
        Assert.Equal(before, await RowAsync(lease, world.Untouched.Id));
    }

    [Fact]
    public async Task Production_rule_reports_AlreadyDeleted_for_a_deleted_touched_order()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        var world = await SeedWorldAsync(lease);
        await NewService(lease, allowTouched: true)
            .DeleteAsync(world.Picked.Id, world.Manager.Id, CancellationToken.None);

        var result = await NewService(lease, allowTouched: false)
            .DeleteAsync(world.Picked.Id, world.Manager.Id, CancellationToken.None);

        Assert.Equal(OrderDeletionResult.AlreadyDeleted, result);
    }

    [Fact]
    public async Task Concurrent_claim_and_delete_of_an_untouched_order_succeed_exactly_once()
    {
        const int iterations = 20;
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        Employee manager;
        await using (var seed = lease.CreateDbContext())
        {
            manager = NewEmployee("racemanager", EmployeeRole.ManagerAdmin);
            seed.Employees.Add(manager);
            await seed.SaveChangesAsync();
        }

        var claimWins = 0;
        var deleteWins = 0;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            // A fresh picker per iteration: a winning claim holds that picker's one active claim.
            var picker = NewEmployee($"racepicker{iteration}", EmployeeRole.Picker);
            var order = NewOrder($"RACE-DELETE-{iteration}", OrderStatus.Ready, Recorded);
            await using (var seed = lease.CreateDbContext())
            {
                seed.Employees.Add(picker);
                seed.Orders.Add(order);
                await seed.SaveChangesAsync();
            }

            await using var claimContext = lease.CreateDbContext();
            await using var deleteContext = lease.CreateDbContext();
            var claimRepository = new OrderRepository(claimContext);
            var deleteRepository = new OrderDeletionRepository(deleteContext);
            // Open both connections first, so the race is between the two statements.
            await claimContext.Database.OpenConnectionAsync();
            await deleteContext.Database.OpenConnectionAsync();

            var start = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var claimTask = Task.Run(async () =>
            {
                await start.Task;
                return await claimRepository.ClaimSpecificAsync(
                    order.Id,
                    picker.Id,
                    CancellationToken.None
                );
            });
            var deleteTask = Task.Run(async () =>
            {
                await start.Task;
                return await deleteRepository.DeleteAsync(
                    order.Id,
                    manager.Id,
                    Now,
                    allowTouched: false,
                    CancellationToken.None
                );
            });
            start.SetResult();
            await Task.WhenAll(claimTask, deleteTask);

            var claimSucceeded = (await claimTask).Succeeded;
            var deleteOutcome = (await deleteTask).Outcome;
            var row = await RowAsync(lease, order.Id);

            if (claimSucceeded)
            {
                claimWins++;
                Assert.Equal(OrderDeletionOutcome.ClaimedBefore, deleteOutcome);
                Assert.Null(row.DeletedAt);
                Assert.Equal(picker.Id, row.ClaimedByEmployeeId);
            }
            else
            {
                deleteWins++;
                Assert.Equal(OrderDeletionOutcome.Deleted, deleteOutcome);
                Assert.Equal(Now, row.DeletedAt);
                Assert.Null(row.ClaimedByEmployeeId);
                Assert.Null(row.FirstClaimedAt);
            }
        }

        output.WriteLine($"Claim won {claimWins} of {iterations}; delete won {deleteWins}.");
        Assert.Equal(iterations, claimWins + deleteWins);
    }

    [Fact]
    public async Task Delete_all_under_the_production_rule_deletes_only_untouched_orders_imported_by_asOf()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        var world = await SeedWorldAsync(lease);
        var touchedBefore = new Dictionary<int, OrderRow>();
        foreach (var order in world.Touched)
        {
            touchedBefore[order.Id] = await RowAsync(lease, order.Id);
        }

        var result = await NewService(lease, allowTouched: false)
            .DeleteAllAsync(world.Manager.Id, world.AsOf, CancellationToken.None);

        Assert.Equal(OrderDeleteAllResult.Done(deletedCount: 1, keptCount: 6), result);
        var untouched = await RowAsync(lease, world.Untouched.Id);
        Assert.Equal(Now, untouched.DeletedAt);
        Assert.Equal(world.Manager.Id, untouched.DeletedByEmployeeId);
        Assert.False(await SlipExistsAsync(lease, world.Untouched.Id));
        foreach (var order in world.Touched)
        {
            Assert.Equal(touchedBefore[order.Id], await RowAsync(lease, order.Id));
        }
        Assert.True(await SlipExistsAsync(lease, world.Packed.Id));
        // Imported after asOf: left visible, with its slip.
        Assert.Null((await RowAsync(lease, world.Late.Id)).DeletedAt);
        Assert.True(await SlipExistsAsync(lease, world.Late.Id));
    }

    [Fact]
    public async Task Delete_all_under_the_stage_rule_deletes_every_order_imported_by_asOf()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        var world = await SeedWorldAsync(lease);

        var result = await NewService(lease, allowTouched: true)
            .DeleteAllAsync(world.Manager.Id, world.AsOf, CancellationToken.None);

        Assert.Equal(OrderDeleteAllResult.Done(deletedCount: 7, keptCount: 0), result);
        foreach (var order in world.All)
        {
            var row = await RowAsync(lease, order.Id);
            Assert.Equal(Now, row.DeletedAt);
            Assert.Equal(world.Manager.Id, row.DeletedByEmployeeId);
            Assert.False(await SlipExistsAsync(lease, order.Id));
        }
        Assert.Equal(2, await AccessCountAsync(lease, world.Packed.Id));
        Assert.Null((await RowAsync(lease, world.Late.Id)).DeletedAt);
        Assert.True(await SlipExistsAsync(lease, world.Late.Id));
        // The claim fields stay (FR-016).
        Assert.Equal(
            world.Picker.Id,
            (await RowAsync(lease, world.Claimed.Id)).ClaimedByEmployeeId
        );
    }

    [Fact]
    public async Task Delete_all_leaves_an_earlier_deletion_record_alone()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        var world = await SeedWorldAsync(lease);
        await new OrderDeletionRepository(lease.CreateDbContext()).DeleteAsync(
            world.Picked.Id,
            world.OtherManager.Id,
            Recorded,
            allowTouched: true,
            CancellationToken.None
        );

        var result = await NewService(lease, allowTouched: true)
            .DeleteAllAsync(world.Manager.Id, world.AsOf, CancellationToken.None);

        Assert.Equal(OrderDeleteAllResult.Done(deletedCount: 6, keptCount: 0), result);
        var picked = await RowAsync(lease, world.Picked.Id);
        Assert.Equal(Recorded, picked.DeletedAt);
        Assert.Equal(world.OtherManager.Id, picked.DeletedByEmployeeId);
    }

    [Fact]
    public async Task Summary_under_the_production_rule_counts_deletable_and_kept_orders()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        var world = await SeedWorldAsync(lease);
        await HideOneDeletedOrderAsync(lease, world);

        var summary = await NewService(lease, allowTouched: false)
            .GetSummaryAsync(CancellationToken.None);

        // Deletable: Untouched and Late (imported before now). Kept: the six touched orders.
        Assert.Equal(Now, summary.AsOf);
        Assert.Equal(2, summary.DeletableCount);
        Assert.Equal(6, summary.KeptCount);
        Assert.Equal(0, summary.ClaimedCount);
        Assert.Equal(0, summary.WithUnresolvedIssueCount);
    }

    [Fact]
    public async Task Summary_under_the_stage_rule_counts_claims_and_unresolved_issues_among_deletable_orders()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        var world = await SeedWorldAsync(lease);
        await HideOneDeletedOrderAsync(lease, world);

        var summary = await NewService(lease, allowTouched: true)
            .GetSummaryAsync(CancellationToken.None);

        // Every visible order. Claimed: Claimed and ClaimedWithoutStamp. Unresolved issue: WithIssue.
        Assert.Equal(8, summary.DeletableCount);
        Assert.Equal(0, summary.KeptCount);
        Assert.Equal(2, summary.ClaimedCount);
        Assert.Equal(1, summary.WithUnresolvedIssueCount);
    }

    // A deleted order, claimed and with an unresolved issue, must not count anywhere.
    private static async Task HideOneDeletedOrderAsync(SqlServerDatabaseLease lease, World world)
    {
        await using var context = lease.CreateDbContext();
        var hidden = NewOrder("DELETION-HIDDEN", OrderStatus.NeedsAttention, Recorded);
        hidden.ClaimedByEmployeeId = world.OtherPicker.Id;
        hidden.ClaimedAt = Recorded;
        hidden.FirstClaimedAt = Recorded;
        hidden.DeletedAt = Recorded;
        hidden.DeletedByEmployeeId = world.Manager.Id;
        hidden.OrderLines.Add(Line("Hidden Card", PickOutcome.HasIssue, world.OtherPicker.Id));
        context.Orders.Add(hidden);
        await context.SaveChangesAsync();
    }

    private static OrderDeletionService NewService(
        SqlServerDatabaseLease lease,
        bool allowTouched
    ) =>
        new(
            new OrderDeletionRepository(lease.CreateDbContext()),
            new OrderDeletionOptions { AllowTouchedOrders = allowTouched },
            new FixedTimeProvider(Now),
            NullLogger<OrderDeletionService>.Instance
        );

    private sealed record World(
        Employee Picker,
        Employee OtherPicker,
        Employee Manager,
        Employee OtherManager,
        Order Untouched,
        Order Claimed,
        Order ClaimedWithoutStamp,
        Order Released,
        Order Picked,
        Order WithIssue,
        Order Packed,
        Order Late,
        DateTimeOffset AsOf
    )
    {
        public IReadOnlyList<Order> Touched =>
            [Claimed, ClaimedWithoutStamp, Released, Picked, WithIssue, Packed];

        /// <summary>Every order imported at or before <see cref="AsOf"/>.</summary>
        public IReadOnlyList<Order> All => [Untouched, .. Touched];
    }

    private static async Task<World> SeedWorldAsync(SqlServerDatabaseLease lease)
    {
        await using var context = lease.CreateDbContext();
        var picker = NewEmployee("deletionpicker", EmployeeRole.Picker);
        var otherPicker = NewEmployee("deletionpicker2", EmployeeRole.Picker);
        var packer = NewEmployee("deletionpacker", EmployeeRole.Picker);
        var manager = NewEmployee("deletionmanager", EmployeeRole.ManagerAdmin);
        var otherManager = NewEmployee("deletionmanager2", EmployeeRole.ManagerAdmin);
        context.Employees.AddRange(picker, otherPicker, packer, manager, otherManager);
        await context.SaveChangesAsync();

        var asOf = Now.AddMinutes(-5);

        var untouched = NewOrder("DELETION-UNTOUCHED", OrderStatus.Ready, asOf.AddHours(-1));
        untouched.OrderLines.Add(Line("Untouched Card"));

        var claimed = NewOrder("DELETION-CLAIMED", OrderStatus.InProgress, asOf.AddHours(-2));
        claimed.ClaimedByEmployeeId = picker.Id;
        claimed.ClaimedAt = Recorded;
        claimed.FirstClaimedAt = Recorded;
        claimed.OrderLines.Add(Line("Claimed Card"));

        // Claimed, but with no FirstClaimedAt: the rule must not rely on the backfill alone.
        var claimedWithoutStamp = NewOrder(
            "DELETION-CLAIMED-NO-STAMP",
            OrderStatus.InProgress,
            asOf.AddHours(-2)
        );
        claimedWithoutStamp.ClaimedByEmployeeId = otherPicker.Id;
        claimedWithoutStamp.ClaimedAt = Recorded;
        claimedWithoutStamp.OrderLines.Add(Line("Claimed No Stamp Card"));

        var released = NewOrder("DELETION-RELEASED", OrderStatus.Ready, asOf.AddHours(-3));
        released.FirstClaimedAt = Recorded;
        released.OrderLines.Add(Line("Released Card"));

        var picked = NewOrder("DELETION-PICKED", OrderStatus.Picked, asOf.AddHours(-4));
        picked.FirstClaimedAt = Recorded;
        picked.OrderLines.Add(Line("Picked Card", PickOutcome.Picked, picker.Id));

        var withIssue = NewOrder("DELETION-ISSUE", OrderStatus.NeedsAttention, asOf);
        withIssue.FirstClaimedAt = Recorded;
        var issueLine = Line("Issue Card", PickOutcome.HasIssue, picker.Id);
        withIssue.OrderLines.Add(issueLine);

        var packed = NewOrder("DELETION-PACKED", OrderStatus.Packed, asOf.AddHours(-5));
        packed.FirstClaimedAt = Recorded;
        packed.PackedAt = Recorded;
        packed.PackedByEmployeeId = packer.Id;
        packed.OrderLines.Add(Line("Packed Card", PickOutcome.Picked, picker.Id));

        // Imported after asOf but before now.
        var late = NewOrder("DELETION-LATE", OrderStatus.Ready, asOf.AddMinutes(1));
        late.OrderLines.Add(Line("Late Card"));

        context.Orders.AddRange(
            untouched,
            claimed,
            claimedWithoutStamp,
            released,
            picked,
            withIssue,
            packed,
            late
        );
        await context.SaveChangesAsync();

        var issue = new PickingIssue
        {
            OrderLineId = issueLine.Id,
            IssueType = PickingIssueType.CardNotFound,
            ReportedByEmployeeId = picker.Id,
            ReportedAt = Recorded,
        };
        context.PickingIssues.Add(issue);
        foreach (var order in new[] { untouched, packed, late })
        {
            context.OrderPackingSlips.Add(
                new OrderPackingSlip
                {
                    OrderId = order.Id,
                    Content = [1, 2, 3],
                    StoredAt = Recorded,
                }
            );
        }
        context.PackingSlipAccesses.AddRange(
            new PackingSlipAccess
            {
                OrderId = packed.Id,
                EmployeeId = packer.Id,
                RetrievedAt = Recorded,
            },
            new PackingSlipAccess
            {
                OrderId = packed.Id,
                EmployeeId = packer.Id,
                RetrievedAt = Recorded.AddMinutes(1),
            }
        );
        await context.SaveChangesAsync();
        issueLine.CurrentPickingIssueId = issue.Id;
        await context.SaveChangesAsync();

        return new World(
            picker,
            otherPicker,
            manager,
            otherManager,
            untouched,
            claimed,
            claimedWithoutStamp,
            released,
            picked,
            withIssue,
            packed,
            late,
            asOf
        );
    }

    private sealed record OrderRow(
        OrderStatus Status,
        int? ClaimedByEmployeeId,
        DateTimeOffset? ClaimedAt,
        DateTimeOffset? FirstClaimedAt,
        DateTimeOffset? PackedAt,
        int? PackedByEmployeeId,
        DateTimeOffset? DeletedAt,
        int? DeletedByEmployeeId
    );

    private static async Task<OrderRow> RowAsync(SqlServerDatabaseLease lease, int orderId)
    {
        await using var context = lease.CreateDbContext();
        return await context
            .Orders.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => new OrderRow(
                order.Status,
                order.ClaimedByEmployeeId,
                order.ClaimedAt,
                order.FirstClaimedAt,
                order.PackedAt,
                order.PackedByEmployeeId,
                order.DeletedAt,
                order.DeletedByEmployeeId
            ))
            .SingleAsync();
    }

    private static async Task<bool> SlipExistsAsync(SqlServerDatabaseLease lease, int orderId)
    {
        await using var context = lease.CreateDbContext();
        return await context
            .OrderPackingSlips.IgnoreQueryFilters()
            .AnyAsync(slip => slip.OrderId == orderId);
    }

    private static async Task<int> AccessCountAsync(SqlServerDatabaseLease lease, int orderId)
    {
        await using var context = lease.CreateDbContext();
        return await context
            .PackingSlipAccesses.IgnoreQueryFilters()
            .CountAsync(access => access.OrderId == orderId);
    }

    private static Employee NewEmployee(string username, EmployeeRole role) =>
        new()
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = username,
            PinHash = "hash",
            Role = role,
            CreatedAt = Recorded,
        };

    private static Order NewOrder(
        string tcgplayerOrderId,
        OrderStatus status,
        DateTimeOffset importedAt
    ) =>
        new()
        {
            TcgplayerOrderId = tcgplayerOrderId,
            Status = status,
            ImportedAt = importedAt,
        };

    private static OrderLine Line(
        string productName,
        PickOutcome? outcome = null,
        int? employeeId = null
    ) =>
        new()
        {
            RawDescription = $"{productName} - 001/100 - Near Mint",
            ProductLine = "Magic",
            ProductName = productName,
            Set = "Test Set",
            Condition = "Near Mint",
            Quantity = 1,
            PickOutcome = outcome,
            PickOutcomeRecordedByEmployeeId = outcome is null ? null : employeeId,
            PickOutcomeRecordedAt = outcome is null ? null : Recorded,
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
