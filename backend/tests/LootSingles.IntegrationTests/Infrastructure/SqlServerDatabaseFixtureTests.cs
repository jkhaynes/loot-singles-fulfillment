using LootSingles.Domain.Employees;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Infrastructure;

[Collection(SqlServerTestCollection.Name)]
public sealed class SqlServerDatabaseFixtureTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task Leases_have_unique_database_names()
    {
        await using var first = await fixture.CreateDatabaseLeaseAsync();
        await using var second = await fixture.CreateDatabaseLeaseAsync();

        Assert.NotEqual(first.DatabaseName, second.DatabaseName);
    }

    [Fact]
    public async Task Lease_applies_migrations_before_it_is_returned()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var context = lease.CreateDbContext();

        var applied = await context.Database.GetAppliedMigrationsAsync();

        Assert.NotEmpty(applied);
        Assert.Equal(context.Database.GetMigrations(), applied);
    }

    [Fact]
    public async Task Concurrent_leases_cannot_observe_each_others_data()
    {
        await using var first = await fixture.CreateDatabaseLeaseAsync();
        await using var second = await fixture.CreateDatabaseLeaseAsync();
        await using var firstContext = first.CreateDbContext();
        await using var secondContext = second.CreateDbContext();

        firstContext.Employees.Add(
            new Employee
            {
                Username = "first.user",
                NormalizedUsername = "FIRST.USER",
                DisplayName = "First User",
                PinHash = "not-a-real-pin-hash",
                Role = EmployeeRole.Picker,
                CreatedAt = DateTimeOffset.UtcNow,
            }
        );
        await firstContext.SaveChangesAsync();

        Assert.True(await firstContext.Employees.AnyAsync());
        Assert.False(await secondContext.Employees.AnyAsync());
    }

    [Fact]
    public async Task Disposing_a_lease_drops_its_database()
    {
        var lease = await fixture.CreateDatabaseLeaseAsync();
        var databaseName = lease.DatabaseName;

        Assert.True(await fixture.DatabaseExistsAsync(databaseName));

        await lease.DisposeAsync();

        Assert.False(await fixture.DatabaseExistsAsync(databaseName));
    }
}
