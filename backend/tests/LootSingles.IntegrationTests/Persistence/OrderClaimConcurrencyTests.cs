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
