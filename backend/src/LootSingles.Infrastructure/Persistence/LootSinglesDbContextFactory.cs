using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace LootSingles.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by EF Core tooling (`dotnet ef migrations`/`dotnet ef database update`)
/// to construct <see cref="LootSinglesDbContext"/> outside of the running application.
/// Not used at runtime and never supplies a fallback database.
/// </summary>
public class LootSinglesDbContextFactory : IDesignTimeDbContextFactory<LootSinglesDbContext>
{
    public LootSinglesDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(LootSinglesDbContextFactory).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("LootSingles");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Configuration key 'ConnectionStrings:LootSingles' is required for EF Core tooling."
            );
        }

        var optionsBuilder = new DbContextOptionsBuilder<LootSinglesDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new LootSinglesDbContext(optionsBuilder.Options);
    }
}
