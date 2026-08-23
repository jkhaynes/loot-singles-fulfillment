using LootSingles.Application.Import;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Persistence;

[Collection(SqlServerTestCollection.Name)]
public sealed class SqlServerSchemaTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task Schema_contains_behaviorally_relevant_unique_and_lookup_indexes()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var connection = new SqlConnection(lease.ConnectionString);
        await connection.OpenAsync();

        var indexes = new Dictionary<string, bool>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.name, i.is_unique
            FROM sys.indexes i
            WHERE i.name IN (
                'IX_Orders_TcgplayerOrderId',
                'IX_OrderLines_OrderId',
                'IX_ImportOrderResults_ImportAttemptId',
                'IX_ImportOrderResults_ResultingOrderId',
                'IX_Employees_NormalizedUsername',
                'IX_EmployeeAuditEvents_ActorEmployeeId',
                'IX_EmployeeAuditEvents_TargetEmployeeId')
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0), reader.GetBoolean(1));
        }

        Assert.Equal(7, indexes.Count);
        Assert.True(indexes["IX_Orders_TcgplayerOrderId"]);
        Assert.True(indexes["IX_Employees_NormalizedUsername"]);
    }

    [Fact]
    public async Task Unique_indexes_reject_duplicate_order_and_normalized_username()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var context = lease.CreateDbContext();
        context.Orders.AddRange(CreateOrder("DUPLICATE"), CreateOrder("DUPLICATE"));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        context.ChangeTracker.Clear();
        context.Employees.AddRange(
            CreateEmployee("first", "SAME"),
            CreateEmployee("second", "SAME")
        );
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Required_foreign_keys_reject_orphans_and_cascade_owned_rows()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var context = lease.CreateDbContext();
        context.OrderLines.Add(CreateLine(orderId: int.MaxValue));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        context.ChangeTracker.Clear();
        var order = CreateOrder("CASCADE");
        order.OrderLines.Add(CreateLine());
        context.Orders.Add(order);
        var attempt = new ImportAttempt { StartedAt = DateTimeOffset.UtcNow };
        attempt.ImportOrderResults.Add(
            new ImportOrderResult
            {
                ImportAttemptId = 0,
                SourceOrderIdentifier = order.TcgplayerOrderId,
                Outcome = ImportOutcome.Rejected,
                FailureCode = FailureType.NoProductLines,
                FailureMessage = "test",
            }
        );
        context.ImportAttempts.Add(attempt);
        await context.SaveChangesAsync();

        context.Orders.Remove(order);
        context.ImportAttempts.Remove(attempt);
        await context.SaveChangesAsync();

        Assert.False(await context.OrderLines.AnyAsync());
        Assert.False(await context.ImportOrderResults.AnyAsync());
    }

    private static Order CreateOrder(string identifier) =>
        new()
        {
            TcgplayerOrderId = identifier,
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow,
        };

    private static OrderLine CreateLine(int orderId = 0) =>
        new()
        {
            OrderId = orderId,
            RawDescription = "raw",
            ProductLine = "Magic",
            ProductName = "Card",
            Set = "Set",
            CollectorNumber = "#1",
            Condition = "Near Mint",
            Quantity = 1,
        };

    private static Employee CreateEmployee(string username, string normalizedUsername) =>
        new()
        {
            Username = username,
            NormalizedUsername = normalizedUsername,
            DisplayName = username,
            PinHash = "hash",
            Role = EmployeeRole.Picker,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
