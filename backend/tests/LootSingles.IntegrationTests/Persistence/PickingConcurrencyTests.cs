using LootSingles.Application.Orders;
using LootSingles.Application.Picking;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LootSingles.IntegrationTests.Persistence;

/// <summary>
/// 015-pick-completion T016: recording a line outcome is claim-gated server-side (FR-010), and a
/// concurrent force-release can never leave the order in a state its lines don't justify (FR-006).
/// Mirrors <see cref="OrderClaimConcurrencyTests"/>.
/// </summary>
[Collection(SqlServerTestCollection.Name)]
public sealed class PickingConcurrencyTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task Concurrent_pick_and_force_release_never_leave_an_unclaimed_order_in_progress()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var pickContext = lease.CreateDbContext();
        await using var releaseContext = lease.CreateDbContext();

        var picker = NewEmployee("pickracer");
        var order = NewOrder("PICK-RELEASE-RACE");
        order.OrderLines.Add(NewLine("Already Picked", PickOutcome.Picked));
        order.OrderLines.Add(NewLine("Being Picked", null));
        pickContext.Employees.Add(picker);
        pickContext.Orders.Add(order);
        await pickContext.SaveChangesAsync();
        order.ClaimedByEmployeeId = picker.Id;
        order.ClaimedAt = DateTimeOffset.UtcNow;
        order.Status = OrderStatus.InProgress;
        await pickContext.SaveChangesAsync();
        var targetLineId = order.OrderLines.Last().Id;

        var pickTask = NewService(pickContext)
            .RecordPickedAsync(order.Id, targetLineId, picker.Id, CancellationToken.None);
        var releaseTask = new OrderRepository(releaseContext).ForceReleaseAsync(
            order.Id,
            CancellationToken.None
        );
        await Task.WhenAll(pickTask, releaseTask);

        await using var verifyContext = lease.CreateDbContext();
        var finalOrder = await verifyContext
            .Orders.AsNoTracking()
            .Include(o => o.OrderLines)
            .SingleAsync(o => o.Id == order.Id);
        var targetLine = finalOrder.OrderLines.Single(line => line.Id == targetLineId);

        Assert.True((await releaseTask).Succeeded);
        Assert.Null(finalOrder.ClaimedByEmployeeId);
        if ((await pickTask).Outcome == PickingOutcome.Success)
        {
            // Pick committed first; the release then re-derived a fully picked order.
            Assert.Equal(PickOutcome.Picked, targetLine.PickOutcome);
            Assert.Equal(OrderStatus.Picked, finalOrder.Status);
        }
        else
        {
            // Release committed first; the pick must have been rejected and fully rolled back.
            Assert.Equal(PickingOutcome.NotYourClaim, (await pickTask).Outcome);
            Assert.Null(targetLine.PickOutcome);
            Assert.Equal(OrderStatus.Ready, finalOrder.Status);
        }
    }

    [Fact]
    public async Task Concurrent_picks_by_claim_holder_and_a_non_holder_only_the_holder_records()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var holderContext = lease.CreateDbContext();
        await using var intruderContext = lease.CreateDbContext();

        var holder = NewEmployee("claimholder");
        var intruder = NewEmployee("nonholder");
        var order = NewOrder("PICK-INTRUDER-RACE");
        order.OrderLines.Add(NewLine("Contested Card", null));
        holderContext.Employees.AddRange(holder, intruder);
        holderContext.Orders.Add(order);
        await holderContext.SaveChangesAsync();
        order.ClaimedByEmployeeId = holder.Id;
        order.ClaimedAt = DateTimeOffset.UtcNow;
        order.Status = OrderStatus.InProgress;
        await holderContext.SaveChangesAsync();
        var lineId = order.OrderLines.Single().Id;

        var results = await Task.WhenAll(
            NewService(holderContext)
                .RecordPickedAsync(order.Id, lineId, holder.Id, CancellationToken.None),
            NewService(intruderContext)
                .RecordPickedAsync(order.Id, lineId, intruder.Id, CancellationToken.None)
        );

        Assert.Equal(PickingOutcome.Success, results[0].Outcome);
        Assert.Equal(PickingOutcome.NotYourClaim, results[1].Outcome);
        await using var verifyContext = lease.CreateDbContext();
        var line = await verifyContext.OrderLines.AsNoTracking().SingleAsync(l => l.Id == lineId);
        Assert.Equal(holder.Id, line.PickOutcomeRecordedByEmployeeId);
    }

    // 015-pick-completion T057 (branch review BR-002): the detail returned to the caller is the
    // state the recording transaction itself committed, so the response, the logged status, and
    // the persisted rows are one observation rather than a later re-read.
    [Fact]
    public async Task Recorded_outcome_returns_the_detail_it_committed()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var context = lease.CreateDbContext();

        var picker = NewEmployee("detailpicker");
        var order = NewOrder("RETURNED-DETAIL");
        order.OrderLines.Add(NewLine("First Card", null));
        order.OrderLines.Add(NewLine("Second Card", null));
        context.Employees.Add(picker);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        order.ClaimedByEmployeeId = picker.Id;
        order.ClaimedAt = DateTimeOffset.UtcNow;
        order.Status = OrderStatus.InProgress;
        await context.SaveChangesAsync();
        var firstLineId = order.OrderLines.First().Id;
        var secondLineId = order.OrderLines.Last().Id;
        var repository = new PickingRepository(context);

        var picked = await repository.RecordOutcomeAsync(
            order.Id,
            firstLineId,
            picker.Id,
            new PickOutcomeChange.Picked(),
            CancellationToken.None
        );

        Assert.Equal(PickingOutcome.Success, picked.Outcome);
        Assert.Equal(OrderStatus.InProgress, picked.Order!.Status);
        Assert.Equal(picker.Id, picked.Order.ClaimedByEmployeeId);
        Assert.Equal(PickOutcome.Picked, LineIn(picked.Order, firstLineId).PickOutcome);
        Assert.Null(LineIn(picked.Order, secondLineId).PickOutcome);
        await AssertMatchesPersistedStateAsync(lease, picked.Order!);

        var reported = await repository.RecordOutcomeAsync(
            order.Id,
            secondLineId,
            picker.Id,
            new PickOutcomeChange.IssueReport(
                PickingIssueType.CardNotFound,
                RequiredQuantity: 2,
                FoundQuantity: 1,
                Note: "Only one in the bin"
            ),
            CancellationToken.None
        );

        Assert.Equal(PickingOutcome.Success, reported.Outcome);
        Assert.Equal(OrderStatus.NeedsAttention, reported.Order!.Status);
        var flaggedLine = LineIn(reported.Order, secondLineId);
        Assert.Equal(PickOutcome.HasIssue, flaggedLine.PickOutcome);
        Assert.Equal(PickingIssueType.CardNotFound, flaggedLine.CurrentIssue!.IssueType);
        Assert.Equal(2, flaggedLine.CurrentIssue.RequiredQuantity);
        Assert.Equal(1, flaggedLine.CurrentIssue.FoundQuantity);
        Assert.Equal("Only one in the bin", flaggedLine.CurrentIssue.Note);
        Assert.Equal(picker.DisplayName, flaggedLine.CurrentIssue.ReportedByEmployeeName);
        await AssertMatchesPersistedStateAsync(lease, reported.Order!);
    }

    private static OrderLineDetail LineIn(OrderDetail detail, int lineId) =>
        detail.Lines.Single(line => line.Id == lineId);

    private static async Task AssertMatchesPersistedStateAsync(
        SqlServerDatabaseLease lease,
        OrderDetail returned
    )
    {
        await using var verifyContext = lease.CreateDbContext();
        var persisted = await verifyContext
            .Orders.AsNoTracking()
            .Include(order => order.OrderLines)
            .SingleAsync(order => order.Id == returned.OrderId);

        Assert.Equal(persisted.Status, returned.Status);
        Assert.Equal(persisted.ClaimedByEmployeeId, returned.ClaimedByEmployeeId);
        Assert.Equal(
            persisted
                .OrderLines.OrderBy(line => line.Id)
                .Select(line => (line.Id, line.PickOutcome)),
            returned.Lines.Select(line => (line.Id, line.PickOutcome))
        );
    }

    private static PickingService NewService(LootSinglesDbContext context) =>
        new(new PickingRepository(context), NullLogger<PickingService>.Instance);

    private static Employee NewEmployee(string username) =>
        new()
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = username,
            PinHash = "hash",
            Role = EmployeeRole.Picker,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static Order NewOrder(string tcgplayerOrderId) =>
        new()
        {
            TcgplayerOrderId = tcgplayerOrderId,
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow,
        };

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
}
