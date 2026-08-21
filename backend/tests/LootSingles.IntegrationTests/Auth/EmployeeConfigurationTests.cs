using LootSingles.Domain.Employees;
using LootSingles.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Auth;

public class EmployeeConfigurationTests
{
    [Fact]
    public async Task AddAsync_CaseInsensitiveDuplicateUsername_ViolatesUniqueConstraint()
    {
        var connectionString = $"Data Source=employees-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var setup = CreateContext(connectionString)) await setup.Database.EnsureCreatedAsync();

        await using (var first = CreateContext(connectionString))
        {
            first.Employees.Add(NewEmployee("jsmith"));
            await first.SaveChangesAsync();
        }

        await using var second = CreateContext(connectionString);
        second.Employees.Add(NewEmployee("JSmith"));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    private static LootSinglesDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<LootSinglesDbContext>().UseSqlite(connectionString).Options);

    private static Employee NewEmployee(string username) => new()
    {
        Username = username,
        NormalizedUsername = username.ToUpperInvariant(),
        DisplayName = username,
        PinHash = "hash",
        Role = EmployeeRole.Picker,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
