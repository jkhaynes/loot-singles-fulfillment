using LootSingles.Application.Orders;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LootSingles.IntegrationTests.Orders;

/// <summary>
/// 021 research R5 / FR-013: the Development host allows deleting touched orders without any
/// local settings file; every other environment gets the strict production rule unless configured.
/// </summary>
public sealed class OrderDeletionConfigurationTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", false)]
    [InlineData("Production", false)]
    public async Task The_environment_decides_whether_touched_orders_may_be_deleted(
        string environment,
        bool expected
    )
    {
        await using var root = new AuthWebApplicationFactory();
        await using var factory = WithEnvironment(root, environment);

        var options = factory.Services.GetRequiredService<OrderDeletionOptions>();

        Assert.Equal(expected, options.AllowTouchedOrders);
    }

    [Fact]
    public async Task A_Development_host_deletes_a_claimed_order()
    {
        await using var root = new AuthWebApplicationFactory();
        await using var factory = WithEnvironment(root, "Development");
        var picker = NewEmployee("devdeletepicker", EmployeeRole.Picker);
        var manager = NewEmployee("devdeletemanager", EmployeeRole.ManagerAdmin);
        var order = new Order
        {
            TcgplayerOrderId = "DEV-DELETE-CLAIMED",
            Status = OrderStatus.InProgress,
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };
        using (var seedScope = factory.Services.CreateScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
            context.Employees.AddRange(picker, manager);
            await context.SaveChangesAsync();
            order.ClaimedByEmployeeId = picker.Id;
            order.ClaimedAt = DateTimeOffset.UtcNow;
            order.FirstClaimedAt = order.ClaimedAt;
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<OrderDeletionService>();
        var result = await service.DeleteAsync(order.Id, manager.Id, CancellationToken.None);

        Assert.Equal(OrderDeletionResult.Deleted, result);
        var context2 = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        Assert.Equal(
            manager.Id,
            await context2
                .Orders.IgnoreQueryFilters()
                .Where(o => o.Id == order.Id)
                .Select(o => o.DeletedByEmployeeId)
                .SingleAsync()
        );
    }

    private static WebApplicationFactory<Program> WithEnvironment(
        WebApplicationFactory<Program> root,
        string environment
    ) => root.WithWebHostBuilder(builder => builder.UseEnvironment(environment));

    private static Employee NewEmployee(string username, EmployeeRole role) =>
        new()
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = username,
            PinHash = "hash",
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
