using LootSingles.Application.Persistence;
using LootSingles.Domain.Employees;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Auth;

public class EmployeeConfigurationTests
{
    [Fact]
    public async Task AddAsync_CaseInsensitiveDuplicateUsername_ViolatesUniqueConstraint()
    {
        await using var lease = await SqlServerContainerFixture.CreateSharedDatabaseLeaseAsync();
        await using (var first = lease.CreateDbContext())
        {
            first.Employees.Add(NewEmployee("jsmith"));
            await first.SaveChangesAsync();
        }

        await using var second = lease.CreateDbContext();
        second.Employees.Add(NewEmployee("JSmith"));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task EmployeeRepositorySaveChangesAsync_CaseInsensitiveDuplicateUsername_ThrowsUniqueConstraintViolationException()
    {
        await using var lease = await SqlServerContainerFixture.CreateSharedDatabaseLeaseAsync();
        await using (var first = lease.CreateDbContext())
        {
            var firstRepository = new EmployeeRepository(first);
            firstRepository.Add(NewEmployee("jsmith"));
            await firstRepository.SaveChangesAsync(CancellationToken.None);
        }

        await using var second = lease.CreateDbContext();
        var secondRepository = new EmployeeRepository(second);
        secondRepository.Add(NewEmployee("JSmith"));

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() =>
            secondRepository.SaveChangesAsync(CancellationToken.None)
        );
    }

    private static Employee NewEmployee(string username) =>
        new()
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = username,
            PinHash = "hash",
            Role = EmployeeRole.Picker,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
