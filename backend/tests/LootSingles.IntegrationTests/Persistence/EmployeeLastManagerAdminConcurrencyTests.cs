using LootSingles.Application.Auth;
using LootSingles.Domain.Employees;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Persistence;

[Collection(SqlServerTestCollection.Name)]
public sealed class EmployeeLastManagerAdminConcurrencyTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task Concurrent_deactivation_of_both_remaining_manager_admins_never_leaves_zero_active()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var firstContext = lease.CreateDbContext();
        await using var secondContext = lease.CreateDbContext();

        var managerA = NewManagerAdmin("racemanagera");
        var managerB = NewManagerAdmin("racemanagerb");
        firstContext.Employees.AddRange(managerA, managerB);
        await firstContext.SaveChangesAsync();

        var firstService = NewService(firstContext);
        var secondService = NewService(secondContext);

        var results = await Task.WhenAll(
            firstService.DeactivateAsync(managerA.Id, managerA.Id, CancellationToken.None),
            secondService.DeactivateAsync(managerB.Id, managerB.Id, CancellationToken.None)
        );

        Assert.Single(results, result => result.Outcome == EmployeeManagementOutcome.Success);
        Assert.Single(
            results,
            result => result.Outcome == EmployeeManagementOutcome.WouldRemoveLastManagerAdmin
        );

        await using var verification = lease.CreateDbContext();
        var remainingActiveManagerAdmins = await verification
            .Employees.AsNoTracking()
            .CountAsync(employee =>
                employee.IsActive && employee.Role == EmployeeRole.ManagerAdmin
            );
        Assert.Equal(1, remainingActiveManagerAdmins);
    }

    private static EmployeeManagementService NewService(LootSinglesDbContext context) =>
        new(new EmployeeRepository(context), new Pbkdf2PinHasher());

    private static Employee NewManagerAdmin(string username) =>
        new()
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = username,
            PinHash = "hash",
            Role = EmployeeRole.ManagerAdmin,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
