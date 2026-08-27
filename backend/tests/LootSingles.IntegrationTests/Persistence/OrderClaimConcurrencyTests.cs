using LootSingles.Application.Orders;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace LootSingles.IntegrationTests.Persistence;

[Collection(SqlServerTestCollection.Name)]
public sealed class OrderClaimConcurrencyTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task Concurrent_pick_next_order_by_two_employees_never_assigns_the_same_order()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var firstContext = lease.CreateDbContext();
        await using var secondContext = lease.CreateDbContext();

        var employeeA = NewEmployee("racera");
        var employeeB = NewEmployee("racerb");
        var orderOne = NewOrder("RACE-ORDER-1");
        var orderTwo = NewOrder("RACE-ORDER-2");
        firstContext.Employees.AddRange(employeeA, employeeB);
        firstContext.Orders.AddRange(orderOne, orderTwo);
        await firstContext.SaveChangesAsync();

        var firstService = NewService(firstContext);
        var secondService = NewService(secondContext);

        var results = await Task.WhenAll(
            firstService.PickNextAsync(employeeA.Id, CancellationToken.None),
            secondService.PickNextAsync(employeeB.Id, CancellationToken.None)
        );

        Assert.All(results, result => Assert.Equal(OrderClaimOutcome.Success, result.Outcome));
        var claimedOrderIds = results.Select(result => result.Order!.Id).ToArray();
        Assert.Equal(2, claimedOrderIds.Distinct().Count());
    }

    [Fact]
    public async Task Concurrent_claim_of_the_same_specific_order_yields_exactly_one_winner()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var firstContext = lease.CreateDbContext();
        await using var secondContext = lease.CreateDbContext();

        var employeeA = NewEmployee("choosera");
        var employeeB = NewEmployee("chooserb");
        var contestedOrder = NewOrder("CHOOSE-RACE-ORDER");
        firstContext.Employees.AddRange(employeeA, employeeB);
        firstContext.Orders.Add(contestedOrder);
        await firstContext.SaveChangesAsync();

        var firstService = NewService(firstContext);
        var secondService = NewService(secondContext);

        var results = await Task.WhenAll(
            firstService.ClaimAsync(contestedOrder.Id, employeeA.Id, CancellationToken.None),
            secondService.ClaimAsync(contestedOrder.Id, employeeB.Id, CancellationToken.None)
        );

        Assert.Single(results, result => result.Outcome == OrderClaimOutcome.Success);
        Assert.Single(results, result => result.Outcome == OrderClaimOutcome.AlreadyClaimed);
    }

    [Fact]
    public async Task Concurrent_pick_next_and_choose_targeting_the_same_order_yields_exactly_one_winner()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var firstContext = lease.CreateDbContext();
        await using var secondContext = lease.CreateDbContext();

        var employeeA = NewEmployee("pickracera");
        var employeeB = NewEmployee("pickracerb");
        var onlyOrder = NewOrder("ONLY-AVAILABLE-ORDER");
        firstContext.Employees.AddRange(employeeA, employeeB);
        firstContext.Orders.Add(onlyOrder);
        await firstContext.SaveChangesAsync();

        var pickNextService = NewService(firstContext);
        var chooseService = NewService(secondContext);

        var pickNextTask = pickNextService.PickNextAsync(employeeA.Id, CancellationToken.None);
        var chooseTask = chooseService.ClaimAsync(
            onlyOrder.Id,
            employeeB.Id,
            CancellationToken.None
        );
        await Task.WhenAll(pickNextTask, chooseTask);

        var outcomes = new[] { (await pickNextTask).Outcome, (await chooseTask).Outcome };
        Assert.Single(outcomes, outcome => outcome == OrderClaimOutcome.Success);
        Assert.Single(
            outcomes,
            outcome =>
                outcome is OrderClaimOutcome.AlreadyClaimed or OrderClaimOutcome.NoOrdersAvailable
        );
    }

    [Fact]
    public async Task Concurrent_release_and_force_release_of_the_same_order_succeeds_exactly_once()
    {
        // The manager side calls IOrderRepository.ForceReleaseAsync directly rather than through
        // OrderClaimService.ForceReleaseAsync, since that service method belongs to a later
        // implementation phase (US5) — this exercises the same underlying conditional-update
        // primitive the eventual service method will call, so the race guarantee proven here holds.
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var firstContext = lease.CreateDbContext();
        await using var secondContext = lease.CreateDbContext();

        var claimant = NewEmployee("releaseracer");
        var claimedOrder = NewOrder("RELEASE-RACE-ORDER");
        firstContext.Employees.Add(claimant);
        firstContext.Orders.Add(claimedOrder);
        await firstContext.SaveChangesAsync();
        var claimResult = await NewService(firstContext)
            .ClaimAsync(claimedOrder.Id, claimant.Id, CancellationToken.None);
        Assert.Equal(OrderClaimOutcome.Success, claimResult.Outcome);

        var releaseService = NewService(firstContext);
        var forceReleaseRepository = new OrderRepository(secondContext);

        var releaseTask = releaseService.ReleaseAsync(
            claimedOrder.Id,
            claimant.Id,
            CancellationToken.None
        );
        var forceReleaseTask = forceReleaseRepository.ForceReleaseAsync(
            claimedOrder.Id,
            CancellationToken.None
        );
        await Task.WhenAll(releaseTask, forceReleaseTask);
        var releaseResult = await releaseTask;
        var forceReleaseAttempt = await forceReleaseTask;

        // Exactly one of the two conditional updates wins the race — never both, never neither.
        Assert.NotEqual(
            releaseResult.Outcome == OrderClaimOutcome.Success,
            forceReleaseAttempt.Succeeded
        );
        if (releaseResult.Outcome != OrderClaimOutcome.Success)
        {
            Assert.Equal(OrderClaimOutcome.NotYourClaim, releaseResult.Outcome);
        }
    }

    private static OrderClaimService NewService(LootSinglesDbContext context) =>
        new(new OrderRepository(context), NullLogger<OrderClaimService>.Instance);

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
}
