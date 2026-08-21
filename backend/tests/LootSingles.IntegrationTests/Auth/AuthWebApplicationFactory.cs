using LootSingles.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LootSingles.IntegrationTests.Auth;

/// <summary>
/// Hosts LootSingles.Api in-process against an isolated in-memory SQLite database (one per
/// instance), so controller tests exercise the real HTTP pipeline (routing, cookie auth
/// middleware, model binding) without depending on a real SQL Server instance.
/// </summary>
public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly string _connectionString;
    private readonly TimeProvider? _timeProvider;

    public AuthWebApplicationFactory(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider;
        _connectionString = $"Data Source=auth-api-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<LootSinglesDbContext>>();
            services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<LootSinglesDbContext>>();
            services.AddDbContext<LootSinglesDbContext>(options => options.UseSqlite(_connectionString));
            if (_timeProvider is not null)
            {
                services.PostConfigure<CookieAuthenticationOptions>(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    options => options.TimeProvider = _timeProvider);
            }
        });
    }

    /// <summary>
    /// Creates the schema on the isolated database (idempotent; safe to call with no seeding too).
    /// </summary>
    public async Task EnsureDatabaseCreatedAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        await context.Database.EnsureCreatedAsync();
    }

    /// <summary>
    /// Runs <paramref name="seed"/> against a scoped <see cref="LootSinglesDbContext"/> and saves
    /// changes, for setting up fixture data before a request is made.
    /// </summary>
    public async Task SeedAsync(Func<LootSinglesDbContext, Task> seed)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        await context.Database.EnsureCreatedAsync();
        await seed(context);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// An HttpClient whose base address is treated as HTTPS by the in-process TestServer, so
    /// Secure-flagged auth cookies (Program.cs: <c>CookieSecurePolicy.Always</c>) are actually sent.
    /// </summary>
    public HttpClient CreateAuthenticatedClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _keepAliveConnection.Dispose();
        }
    }
}
