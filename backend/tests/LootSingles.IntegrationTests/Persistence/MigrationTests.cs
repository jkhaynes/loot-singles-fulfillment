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
                "20260919205246_AddPickCompletion",
                "20260921155303_AddPackingHandoff",
                // 019 T034. Additive only — one CreateTable for the framework-owned Data Protection
                // key ring, so restoring the previous application version after a failed release
                // still works: it simply ignores a table it does not know about (FR-016).
                "20260923044233_AddDataProtectionKeys",
            ],
            migrations
        );
        Assert.Equal(migrations, applied);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    /// <summary>
    /// Branch review BR-012: the concurrency suites only prove something about production if they
    /// run under production's isolation level. Azure SQL Database has READ_COMMITTED_SNAPSHOT on
    /// by default and the SQL Server container has it off, so
    /// <see cref="SqlServerDatabaseLease"/> turns it on at creation. This guards that setup from
    /// being silently dropped.
    /// </summary>
    [Fact]
    public async Task Leased_database_uses_read_committed_snapshot_like_azure_sql()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var context = lease.CreateDbContext();

        var isReadCommittedSnapshotOn = await context
            .Database.SqlQuery<int>(
                $"SELECT CAST(is_read_committed_snapshot_on AS int) AS Value FROM sys.databases WHERE name = DB_NAME()"
            )
            .SingleAsync();

        Assert.Equal(1, isReadCommittedSnapshotOn);
    }
}
