using System.Collections.Concurrent;
using System.Data.Common;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LootSingles.IntegrationTests.Persistence;

[Collection(SqlServerTestCollection.Name)]
public sealed class SqlServerQueryTranslationTests(SqlServerContainerFixture fixture)
{
    private static readonly DateTimeOffset SharedTime = new(
        2026,
        8,
        22,
        12,
        0,
        0,
        TimeSpan.FromHours(5)
    );

    [Fact]
    public async Task Order_repository_orders_offsets_and_ties_in_sql()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await SeedOrdersAsync(lease);
        var (context, commands) = CreateObservedContext(lease);
        await using (context)
        {
            var result = await new OrderRepository(context).GetAllAsync(CancellationToken.None);
            Assert.Equal(["A", "B", "C"], result.Select(row => row.TcgplayerOrderId));
        }

        Assert.Contains("ORDER BY", commands.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dashboard_repository_orders_offsets_and_ties_in_sql()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await SeedOrdersAsync(lease);
        var (context, commands) = CreateObservedContext(lease);
        await using (context)
        {
            var result = await new DashboardRepository(context).GetReadyOrderSummariesAsync(
                CancellationToken.None
            );
            Assert.Equal(["C", "A", "B"], result.Select(row => row.TcgplayerOrderId));
        }

        Assert.Contains("ORDER BY", commands.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Employee_repository_orders_audit_offsets_and_ties_in_sql()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using (var seed = lease.CreateDbContext())
        {
            seed.EmployeeAuditEvents.AddRange(
                CreateAudit(7, SharedTime.AddMinutes(-1)),
                CreateAudit(7, SharedTime),
                CreateAudit(7, SharedTime)
            );
            await seed.SaveChangesAsync();
        }

        var (context, commands) = CreateObservedContext(lease);
        await using (context)
        {
            var result = await new EmployeeRepository(context).GetAuditEventsAsync(
                7,
                CancellationToken.None
            );
            Assert.Equal(
                result.OrderByDescending(row => row.OccurredAt).ThenByDescending(row => row.Id),
                result
            );
        }

        Assert.Contains("ORDER BY", commands.Single(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task SeedOrdersAsync(SqlServerDatabaseLease lease)
    {
        await using var context = lease.CreateDbContext();
        context.Orders.AddRange(
            CreateOrder("A", SharedTime),
            CreateOrder("B", SharedTime),
            CreateOrder("C", SharedTime.AddMinutes(-1))
        );
        await context.SaveChangesAsync();
    }

    private static (
        LootSinglesDbContext Context,
        ConcurrentBag<string> Commands
    ) CreateObservedContext(SqlServerDatabaseLease lease)
    {
        var interceptor = new CommandCaptureInterceptor();
        var options = new DbContextOptionsBuilder<LootSinglesDbContext>()
            .UseSqlServer(lease.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        return (new LootSinglesDbContext(options), interceptor.Commands);
    }

    private static Order CreateOrder(string identifier, DateTimeOffset importedAt) =>
        new()
        {
            TcgplayerOrderId = identifier,
            Status = OrderStatus.Ready,
            ImportedAt = importedAt,
            OrderLines = [CreateLine()],
        };

    private static OrderLine CreateLine() =>
        new()
        {
            RawDescription = "raw",
            ProductLine = "Magic",
            ProductName = "Card",
            Set = "Set",
            CollectorNumber = "#1",
            Condition = "Near Mint",
            Quantity = 1,
        };

    private static EmployeeAuditEvent CreateAudit(int employeeId, DateTimeOffset occurredAt) =>
        new()
        {
            ActorEmployeeId = employeeId,
            ActionType = EmployeeAuditActionType.Login,
            OccurredAt = occurredAt,
        };

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public ConcurrentBag<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
