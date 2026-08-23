using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace LootSingles.IntegrationTests.Infrastructure;

public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    public const string Image = "mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04";

    private static readonly MsSqlContainer Container = new MsSqlBuilder(Image).Build();
    private static readonly SemaphoreSlim StartLock = new(1, 1);
    private static Task? _startTask;

    static SqlServerContainerFixture()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (_startTask?.IsCompletedSuccessfully == true)
            {
                Container.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        };
    }

    public async Task InitializeAsync()
    {
        try
        {
            await EnsureStartedAsync();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Could not start the SQL Server test container using image '{Image}'. "
                    + "Confirm that a Docker-compatible runtime is running and the image is available. "
                    + "Connection details are intentionally omitted.",
                exception
            );
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public Task<SqlServerDatabaseLease> CreateDatabaseLeaseAsync(
        CancellationToken cancellationToken = default
    ) => CreateSharedDatabaseLeaseAsync(cancellationToken);

    internal static async Task<SqlServerDatabaseLease> CreateSharedDatabaseLeaseAsync(
        CancellationToken cancellationToken = default
    )
    {
        await EnsureStartedAsync();
        return await SqlServerDatabaseLease.CreateAsync(
            Container.GetConnectionString(),
            cancellationToken
        );
    }

    internal Task<SqlServerDatabaseLease> CreateDatabaseLeaseAsync(
        string databaseName,
        Func<CancellationToken, Task> afterDatabaseCreated,
        CancellationToken cancellationToken = default
    ) =>
        SqlServerDatabaseLease.CreateAsync(
            Container.GetConnectionString(),
            cancellationToken,
            databaseName,
            afterDatabaseCreated
        );

    internal async Task<bool> DatabaseExistsAsync(
        string databaseName,
        CancellationToken cancellationToken = default
    )
    {
        var builder = new SqlConnectionStringBuilder(Container.GetConnectionString())
        {
            InitialCatalog = "master",
        };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @databaseName";
        command.Parameters.AddWithValue("@databaseName", databaseName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task EnsureStartedAsync()
    {
        if (_startTask is not null)
        {
            await _startTask;
            return;
        }

        await StartLock.WaitAsync();
        try
        {
            _startTask ??= Container.StartAsync();
        }
        finally
        {
            StartLock.Release();
        }

        await _startTask;
    }
}
