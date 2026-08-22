using System.Collections.Concurrent;
using LootSingles.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Infrastructure;

public sealed class SqlServerDatabaseLease : IAsyncDisposable
{
    private readonly string _masterConnectionString;
    private readonly string _databaseConnectionString;
    private readonly ConcurrentBag<LootSinglesDbContext> _contexts = [];
    private int _disposed;

    private SqlServerDatabaseLease(string masterConnectionString, string databaseName)
    {
        _masterConnectionString = masterConnectionString;
        DatabaseName = databaseName;
        _databaseConnectionString = new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;
    }

    public string DatabaseName { get; }

    internal static async Task<SqlServerDatabaseLease> CreateAsync(
        string containerConnectionString,
        CancellationToken cancellationToken
    )
    {
        var masterConnectionString = new SqlConnectionStringBuilder(containerConnectionString)
        {
            InitialCatalog = "master",
        }.ConnectionString;
        var databaseName = $"loot_singles_test_{Guid.NewGuid():N}";
        var lease = new SqlServerDatabaseLease(masterConnectionString, databaseName);

        try
        {
            await lease.ExecuteMasterCommandAsync(
                $"CREATE DATABASE [{databaseName}]",
                cancellationToken
            );
            await using var context = lease.CreateUntrackedDbContext();
            await context.Database.MigrateAsync(cancellationToken);
            return lease;
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }
    }

    public LootSinglesDbContext CreateDbContext()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var context = CreateUntrackedDbContext();
        _contexts.Add(context);
        return context;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        while (_contexts.TryTake(out var context))
        {
            await context.DisposeAsync();
        }

        var databaseConnection = new SqlConnection(_databaseConnectionString);
        SqlConnection.ClearPool(databaseConnection);
        await databaseConnection.DisposeAsync();

        await ExecuteMasterCommandAsync(
            $"IF DB_ID(N'{DatabaseName}') IS NOT NULL BEGIN "
                + $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; "
                + $"DROP DATABASE [{DatabaseName}]; END",
            CancellationToken.None
        );
    }

    private LootSinglesDbContext CreateUntrackedDbContext()
    {
        var options = new DbContextOptionsBuilder<LootSinglesDbContext>()
            .UseSqlServer(_databaseConnectionString)
            .Options;
        return new LootSinglesDbContext(options);
    }

    private async Task ExecuteMasterCommandAsync(
        string commandText,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqlConnection(_masterConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
