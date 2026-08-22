using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LootSingles.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by EF Core tooling (`dotnet ef migrations`/`dotnet ef database update`)
/// to construct <see cref="LootSinglesDbContext"/> outside of the running application, which does
/// not yet register the DbContext in its own dependency injection container.
/// Not used at runtime.
/// </summary>
public class LootSinglesDbContextFactory : IDesignTimeDbContextFactory<LootSinglesDbContext>
{
    public LootSinglesDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LootSinglesDbContext>();
        optionsBuilder.UseSqlServer(
            @"Server=(localdb)\MSSQLLocalDB;Database=LootSinglesFulfillment.Dev;Trusted_Connection=True;MultipleActiveResultSets=true"
        );

        return new LootSinglesDbContext(optionsBuilder.Options);
    }
}
