using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Persistence;

[Collection(SqlServerTestCollection.Name)]
public sealed class MigrationTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task Clean_database_has_all_migrations_in_assembly_order_and_matches_current_model()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var context = lease.CreateDbContext();

        var migrations = context.Database.GetMigrations().ToArray();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();

        Assert.Equal(
            [
                "20260820212459_InitialCreate",
                "20260821170809_AddEmployeeAuthentication",
                "20260826201114_AddOrderClaiming",
            ],
            migrations
        );
        Assert.Equal(migrations, applied);
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
