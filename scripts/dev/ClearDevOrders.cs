#:package Microsoft.Data.SqlClient@6.1.1

// DEV ONLY. Clear-DevOrders.ps1 runs this after its dev-database check; do not run it directly.
// It executes clear-dev-orders.sql with Microsoft.Data.SqlClient, which (unlike the
// System.Data.SqlClient built into PowerShell) accepts Entra connection strings such as
// "Authentication=Active Directory Default". The connection string arrives in an environment
// variable so it never appears on a command line, and is never printed.
//
// Usage: dotnet run ClearDevOrders.cs -- preview|delete <path to clear-dev-orders.sql>

using System.Data;
using Microsoft.Data.SqlClient;

if (args is not [var mode, var sqlPath] || mode is not ("preview" or "delete"))
{
    Console.Error.WriteLine("Usage: ClearDevOrders.cs preview|delete <clear-dev-orders.sql>");
    return 2;
}

var connectionString = Environment.GetEnvironmentVariable("LOOT_CLEAR_DEV_ORDERS_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("LOOT_CLEAR_DEV_ORDERS_CONNECTION is not set.");
    return 2;
}

try
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = await File.ReadAllTextAsync(sqlPath);
    command.CommandTimeout = 120;
    command.Parameters.Add(
        new SqlParameter("@Preview", SqlDbType.Bit) { Value = mode == "preview" }
    );
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"  {reader.GetString(0), -22}{reader.GetInt32(1), 8}");
    }

    return 0;
}
catch (SqlException exception)
{
    Console.Error.WriteLine($"SQL error {exception.Number}: {exception.Message}");
    return 1;
}
