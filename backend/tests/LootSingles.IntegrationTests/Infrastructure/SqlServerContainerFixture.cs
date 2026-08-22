using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace LootSingles.IntegrationTests.Infrastructure;

public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    public const string Image = "mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04";

    private readonly MsSqlContainer _container = new MsSqlBuilder(Image).Build();

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
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

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public Task<SqlServerDatabaseLease> CreateDatabaseLeaseAsync(
        CancellationToken cancellationToken = default
    ) => SqlServerDatabaseLease.CreateAsync(_container.GetConnectionString(), cancellationToken);

    internal async Task<bool> DatabaseExistsAsync(
        string databaseName,
        CancellationToken cancellationToken = default
    )
    {
        var builder = new SqlConnectionStringBuilder(_container.GetConnectionString())
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
}
