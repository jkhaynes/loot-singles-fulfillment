using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LootSingles.IntegrationTests.Auth;

/// <summary>
/// Hosts LootSingles.Api in-process against an isolated migrated SQL Server database lease.
/// </summary>
public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqlServerDatabaseLease _databaseLease;
    private readonly TimeProvider? _timeProvider;

    public AuthWebApplicationFactory(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider;
        _databaseLease = SqlServerContainerFixture
            .CreateSharedDatabaseLeaseAsync()
            .GetAwaiter()
            .GetResult();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:LootSingles", _databaseLease.ConnectionString);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<LootSinglesDbContext>>();
            services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<LootSinglesDbContext>>();
            services.AddDbContext<LootSinglesDbContext>(options =>
                options.UseSqlServer(_databaseLease.ConnectionString)
            );
            if (_timeProvider is not null)
            {
                services.PostConfigure<CookieAuthenticationOptions>(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    options => options.TimeProvider = _timeProvider
                );
            }
        });
    }

    /// <summary>
    /// Ensures the already-migrated lease can be resolved by the application host.
    /// </summary>
    public async Task EnsureDatabaseCreatedAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        await context.Database.CanConnectAsync();
    }

    /// <summary>
    /// Runs <paramref name="seed"/> against a scoped <see cref="LootSinglesDbContext"/> and saves
    /// changes, for setting up fixture data before a request is made.
    /// </summary>
    public async Task SeedAsync(Func<LootSinglesDbContext, Task> seed)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        await seed(context);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// An HttpClient whose base address is treated as HTTPS by the in-process TestServer, so
    /// Secure-flagged auth cookies (Program.cs: <c>CookieSecurePolicy.Always</c>) are actually sent.
    /// </summary>
    public HttpClient CreateAuthenticatedClient() =>
        CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }
        );

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _databaseLease.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
