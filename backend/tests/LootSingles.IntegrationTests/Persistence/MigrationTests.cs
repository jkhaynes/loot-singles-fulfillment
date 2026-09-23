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

    /// <summary>
    /// 019 BR-003 / T055 / FR-016.
    ///
    /// The automatic rollback in FR-017 restores the previous container image and nothing else —
    /// there is no schema rollback, by design (contracts/deployment.md). That is only safe while
    /// every migration is backward-compatible with the version before it: a migration that drops or
    /// renames something would leave the restored application unable to read its own database, and
    /// it would happen during an incident, which is the worst moment to discover it.
    ///
    /// <para><b>This guard passes the day it is written</b>, because every current migration is
    /// additive. That is the intended state, not a weak test — it is here to fail the first time
    /// somebody generates a destructive migration, not to demonstrate a present defect.</para>
    ///
    /// <para>If a destructive change is ever genuinely required, it is a two-release operation:
    /// stop writing the column first, release, then drop it in the next one. Deleting this test is
    /// not the way past it.</para>
    /// </summary>
    [Fact]
    public void Migrations_are_additive_so_restoring_the_previous_image_still_works()
    {
        var migrationsDirectory = Path.Combine(
            FindRepositoryRoot(),
            "backend",
            "src",
            "LootSingles.Infrastructure",
            "Persistence",
            "Migrations"
        );

        // Designer files and the model snapshot describe the model rather than the operations, so
        // only the migration bodies are read.
        var migrations = Directory
            .EnumerateFiles(migrationsDirectory, "*.cs")
            .Where(path =>
                !path.EndsWith(".Designer.cs", StringComparison.Ordinal)
                && !path.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal)
            )
            .ToArray();

        Assert.NotEmpty(migrations);

        foreach (var migration in migrations)
        {
            var up = UpMethodOf(File.ReadAllText(migration));
            foreach (var destructive in new[] { "DropColumn", "DropTable", "RenameColumn" })
            {
                Assert.DoesNotContain(destructive, up, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Only <c>Up</c> is scanned. EF scaffolds a <c>Down</c> that drops whatever <c>Up</c> created,
    /// so every migration here contains a <c>DropTable</c> or <c>DropColumn</c> in its <c>Down</c> —
    /// and none of it ever runs, because <c>MigrateAsync</c> only moves forward and this deployment
    /// has no schema-rollback step at all. Scanning the whole file would flag all six existing
    /// migrations and teach the next person to delete the test.
    /// </summary>
    private static string UpMethodOf(string migration)
    {
        var start = migration.IndexOf("void Up(", StringComparison.Ordinal);
        Assert.True(start >= 0, "Migration has no Up method.");

        var end = migration.IndexOf("void Down(", start, StringComparison.Ordinal);
        return end < 0 ? migration[start..] : migration[start..end];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
