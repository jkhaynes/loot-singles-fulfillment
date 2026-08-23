using LootSingles.Domain.Orders;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Persistence;

[Collection(SqlServerTestCollection.Name)]
public sealed class SqlServerTransactionTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task Explicit_transaction_rollback_removes_saved_changes()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using (var context = lease.CreateDbContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.Orders.Add(CreateOrder("ROLLBACK"));
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verification = lease.CreateDbContext();
        Assert.False(await verification.Orders.AnyAsync());
    }

    [Fact]
    public async Task Failed_save_is_atomic_across_multiple_entities()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using (var seed = lease.CreateDbContext())
        {
            seed.Orders.Add(CreateOrder("EXISTING"));
            await seed.SaveChangesAsync();
        }

        await using (var context = lease.CreateDbContext())
        {
            context.Orders.AddRange(CreateOrder("NEW"), CreateOrder("EXISTING"));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        await using var verification = lease.CreateDbContext();
        Assert.False(await verification.Orders.AnyAsync(order => order.TcgplayerOrderId == "NEW"));
    }

    private static Order CreateOrder(string identifier) =>
        new()
        {
            TcgplayerOrderId = identifier,
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow,
        };
}
