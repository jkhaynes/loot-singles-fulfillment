using System.Text.Json.Nodes;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Auth;
using LootSingles.IntegrationTests.ImportUi;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LootSingles.IntegrationTests.Import;

/// <summary>
/// T034 (FR-017, SC-004): after a full stub-backed API import, no invented customer or shipping
/// value from <c>order-details.json</c> is in any column of any table or in any log message, and
/// no token, key or raw response body was logged.
/// </summary>
public sealed class TcgplayerPiiTests
{
    // Values shorter than this ("US", "ZZ", "Test") would match unrelated text by chance, so they
    // are compared against the whole column value instead of searched for inside it.
    private const int MinimumSubstringLength = 6;

    private static readonly string[] SecretAndBodyMarkers =
    [
        "synthetic-", // the fake keys, the store access token and the fixture bearer token
        "access_token",
        "\"orderNumber\"",
        "\"productCount\"",
        "\"results\"",
    ];

    [Fact]
    public async Task A_full_API_import_stores_and_logs_no_customer_or_shipping_value_token_key_or_body()
    {
        var piiValues = InventedCustomerAndShippingValues();
        // Guard the guard: the fixture really carries the values this test searches for.
        Assert.Contains("test.customer@example.test", piiValues);
        Assert.Contains("1 Example St", piiValues);

        var logs = new CapturingLoggerProvider();
        await using var root = new AuthWebApplicationFactory();
        var stub = new TcgplayerStubHandler();
        await using var stubbed = ImportUiTestSupport.WithTcgplayerStub(root, stub);
        await using var factory = stubbed.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Trace);
                    logging.AddProvider(logs);
                })
            )
        );
        using var client = await ImportUiTestSupport.LoginAsync(factory);

        var lines = await ImportUiTestSupport.PostTcgplayerAsync(client);

        // The import really ran: eight of the ten fixture orders import.
        Assert.Contains("\"succeededCount\":8", lines[^1]);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        var connectionString = context.Database.GetConnectionString()!;
        var columns = await ColumnsAsync(connectionString);
        Assert.Contains(
            columns,
            column => column is { Table: "Orders", Column: "TcgplayerOrderId" }
        );
        Assert.Contains(columns, column => column.Table == "OrderLines");

        var leaks = new List<string>();
        foreach (var column in columns)
        {
            foreach (var value in piiValues)
            {
                if (await ColumnHoldsAsync(connectionString, column, value))
                    leaks.Add($"{column.Table}.{column.Column}");
            }
        }
        Assert.Empty(leaks.Distinct());

        Assert.NotEmpty(logs.Entries);
        foreach (var entry in logs.Entries)
        {
            var texts = new List<string> { entry.Message, entry.Exception?.ToString() ?? "" };
            texts.AddRange(entry.State.Select(pair => pair.Value?.ToString() ?? ""));
            foreach (var text in texts)
            {
                Assert.DoesNotContain(piiValues, value => Holds(text, value));
                Assert.DoesNotContain(
                    SecretAndBodyMarkers,
                    marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)
                );
            }
        }
    }

    // Short values are matched whole; longer ones anywhere inside the text.
    private static bool Holds(string text, string value) =>
        value.Length >= MinimumSubstringLength
            ? text.Contains(value, StringComparison.OrdinalIgnoreCase)
            : string.Equals(text, value, StringComparison.OrdinalIgnoreCase);

    // Every leaf under customer and shippingAddress, for every order in the fixture.
    private static List<string> InventedCustomerAndShippingValues()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Tcgplayer",
            "order-details.json"
        );
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var order in JsonNode.Parse(File.ReadAllText(path))!["results"]!.AsArray())
        {
            foreach (var section in new[] { "customer", "shippingAddress" })
                Collect(order![section], values);
        }

        return [.. values];
    }

    private static void Collect(JsonNode? node, HashSet<string> values)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var child in obj)
                    Collect(child.Value, values);
                break;
            case JsonArray array:
                foreach (var child in array)
                    Collect(child, values);
                break;
            case JsonValue leaf:
                values.Add(leaf.ToString());
                break;
        }
    }

    private static async Task<List<ColumnRef>> ColumnsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            SELECT c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE
            FROM INFORMATION_SCHEMA.COLUMNS c
            JOIN INFORMATION_SCHEMA.TABLES t
              ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
            WHERE t.TABLE_TYPE = 'BASE TABLE'
            """,
            connection
        );
        var columns = new List<ColumnRef>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(
                new ColumnRef(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3)
                )
            );
        }

        return columns;
    }

    private static async Task<bool> ColumnHoldsAsync(
        string connectionString,
        ColumnRef column,
        string value
    )
    {
        // Binary columns are searched as their raw bytes read as text; everything else as its text.
        var cast = column.DataType is "varbinary" or "binary" or "image"
            ? "varchar(max)"
            : "nvarchar(max)";
        var expression = $"CAST([{column.Column}] AS {cast})";
        var predicate =
            value.Length >= MinimumSubstringLength
                ? $"CHARINDEX(@value, {expression}) > 0"
                : $"{expression} = @value";
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"SELECT COUNT(*) FROM [{column.Schema}].[{column.Table}] WHERE {predicate}",
            connection
        );
        command.Parameters.AddWithValue("@value", value);
        return (int)(await command.ExecuteScalarAsync())! > 0;
    }

    private sealed record ColumnRef(string Schema, string Table, string Column, string DataType);

    // Hands every category a CapturingLogger, so one list holds the whole host's log output.
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly Lock _gate = new();
        private readonly List<ImportTestSupport.CapturingLogger<object>> _loggers = [];

        public IReadOnlyList<ImportTestSupport.LogEntry> Entries
        {
            get
            {
                lock (_gate)
                {
                    return _loggers.SelectMany(logger => logger.Entries.ToArray()).ToArray();
                }
            }
        }

        public ILogger CreateLogger(string categoryName)
        {
            var logger = new ImportTestSupport.CapturingLogger<object>();
            lock (_gate)
            {
                _loggers.Add(logger);
            }

            return logger;
        }

        public void Dispose() { }
    }
}
