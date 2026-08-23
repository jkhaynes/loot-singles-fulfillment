using LootSingles.Application.Persistence;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Infrastructure;

namespace LootSingles.IntegrationTests.Persistence;

[Collection(SqlServerTestCollection.Name)]
public sealed class SqlServerConcurrencyTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task Concurrent_duplicate_orders_have_one_success_and_one_stable_failure()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var firstContext = lease.CreateDbContext();
        await using var secondContext = lease.CreateDbContext();
        var first = new ImportRepository(firstContext);
        var second = new ImportRepository(secondContext);
        first.AddOrder(CreateOrder("CONCURRENT"));
        second.AddOrder(CreateOrder("CONCURRENT"));

        var outcomes = await Task.WhenAll(SaveOutcomeAsync(first), SaveOutcomeAsync(second));

        Assert.Equal(1, outcomes.Count(outcome => outcome is null));
        Assert.Single(outcomes.OfType<UniqueConstraintViolationException>());
    }

    [Fact]
    public async Task Concurrent_normalized_usernames_have_one_success_and_one_stable_failure()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var firstContext = lease.CreateDbContext();
        await using var secondContext = lease.CreateDbContext();
        var first = new EmployeeRepository(firstContext);
        var second = new EmployeeRepository(secondContext);
        first.Add(CreateEmployee("first"));
        second.Add(CreateEmployee("second"));

        var outcomes = await Task.WhenAll(SaveOutcomeAsync(first), SaveOutcomeAsync(second));

        Assert.Equal(1, outcomes.Count(outcome => outcome is null));
        Assert.Single(outcomes.OfType<UniqueConstraintViolationException>());
    }

    private static async Task<Exception?> SaveOutcomeAsync(ImportRepository repository)
    {
        try
        {
            await repository.SaveChangesAsync(CancellationToken.None);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<Exception?> SaveOutcomeAsync(EmployeeRepository repository)
    {
        try
        {
            await repository.SaveChangesAsync(CancellationToken.None);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static Order CreateOrder(string identifier) =>
        new()
        {
            TcgplayerOrderId = identifier,
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow,
        };

    private static Employee CreateEmployee(string username) =>
        new()
        {
            Username = username,
            NormalizedUsername = "SAME",
            DisplayName = username,
            PinHash = "hash",
            Role = EmployeeRole.Picker,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
