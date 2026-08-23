using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LootSingles.IntegrationTests.Auth;

public sealed class AuthWebApplicationFactoryTests
{
    [Fact]
    public async Task Independently_live_factories_use_isolated_migrated_sql_server_databases()
    {
        await using var first = new AuthWebApplicationFactory();
        await using var second = new AuthWebApplicationFactory();
        await Task.WhenAll(
            first.SeedAsync(context =>
            {
                context.Employees.Add(CreateEmployee("first"));
                context.Orders.Add(CreateOrder("FIRST-ORDER"));
                return Task.CompletedTask;
            }),
            second.SeedAsync(context =>
            {
                context.Employees.Add(CreateEmployee("second"));
                context.Orders.Add(CreateOrder("SECOND-ORDER"));
                return Task.CompletedTask;
            })
        );

        using var firstScope = first.Services.CreateScope();
        using var secondScope = second.Services.CreateScope();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", firstContext.Database.ProviderName);
        Assert.True(
            await firstContext.Employees.AnyAsync(employee => employee.Username == "first")
        );
        Assert.False(
            await firstContext.Employees.AnyAsync(employee => employee.Username == "second")
        );
        Assert.True(
            await secondContext.Employees.AnyAsync(employee => employee.Username == "second")
        );
        Assert.False(
            await secondContext.Employees.AnyAsync(employee => employee.Username == "first")
        );
        Assert.True(
            await firstContext.Orders.AnyAsync(order => order.TcgplayerOrderId == "FIRST-ORDER")
        );
        Assert.False(
            await firstContext.Orders.AnyAsync(order => order.TcgplayerOrderId == "SECOND-ORDER")
        );
        Assert.True(
            await secondContext.Orders.AnyAsync(order => order.TcgplayerOrderId == "SECOND-ORDER")
        );
        Assert.False(
            await secondContext.Orders.AnyAsync(order => order.TcgplayerOrderId == "FIRST-ORDER")
        );
        Assert.NotEqual(
            firstContext.Database.GetConnectionString(),
            secondContext.Database.GetConnectionString()
        );
    }

    private static Employee CreateEmployee(string username) =>
        new()
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = username,
            PinHash = "hash",
            Role = EmployeeRole.Picker,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static Order CreateOrder(string identifier) =>
        new()
        {
            TcgplayerOrderId = identifier,
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow,
        };
}
