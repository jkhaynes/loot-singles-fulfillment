using System.Net;
using System.Net.Http.Json;
using LootSingles.Api.Controllers;
using LootSingles.Domain.Employees;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LootSingles.IntegrationTests.Hosting;

/// <summary>
/// 019 T032 / research.md §5 / FR-026.
///
/// ASP.NET Core's Data Protection system encrypts the session cookie, and its key ring defaults to
/// memory. That is survivable on a long-running server and not survivable here: the container scales
/// to zero when idle, which is what keeps it inside the free compute grant. With keys in memory,
/// every scale-to-zero invalidates every cookie — so a picker who takes a coffee break comes back
/// signed out, not just one who is working during a release.
///
/// Two hosts sharing one database stand in for "the same application before and after a restart".
/// </summary>
[Collection(SqlServerTestCollection.Name)]
public sealed class DataProtectionPersistenceTests(SqlServerContainerFixture fixture)
{
    /// <summary>
    /// The user-facing requirement (FR-026), kept as a regression guard — but read the caveat.
    ///
    /// <para><b>This test cannot fail on a Windows developer machine, even without the fix.</b>
    /// Data Protection falls back to a key ring under the user profile when no persistence is
    /// configured, so two in-process hosts read the same keys from disk and the cookie survives. A
    /// Linux container has no such shared location and an ephemeral filesystem, so keys are
    /// memory-only per instance and the cookie does not survive — which is the production failure
    /// this feature exists to fix.</para>
    ///
    /// <para>The Red → Green pair is therefore
    /// <see cref="Keys_are_written_to_the_database_rather_than_held_in_memory"/>, which fails with
    /// "Invalid object name 'DataProtectionKeys'" until the key ring is actually persisted to SQL.
    /// This test guards the behaviour those keys exist to provide.</para>
    /// </summary>
    [Fact]
    public async Task Cookie_from_one_instance_is_accepted_by_a_new_instance_sharing_the_database()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await SeedEmployeeAsync(lease, "keeper", "1234");

        string cookie;
        await using (var first = new SharedDatabaseFactory(lease.ConnectionString))
        {
            using var client = first.CreateHttpsClient();
            var login = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest("keeper", "1234")
            );
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        }

        // A brand new host, as if the container had scaled to zero and come back.
        await using var second = new SharedDatabaseFactory(lease.ConnectionString);
        using var secondClient = second.CreateHttpsClient();
        secondClient.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        var me = await secondClient.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Keys_are_written_to_the_database_rather_than_held_in_memory()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await SeedEmployeeAsync(lease, "writer", "1234");

        await using (var host = new SharedDatabaseFactory(lease.ConnectionString))
        {
            using var client = host.CreateHttpsClient();
            // Signing in forces the key ring to be created and used.
            var login = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest("writer", "1234")
            );
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        await using var context = lease.CreateDbContext();
        var keyCount = await context
            .Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM [DataProtectionKeys]")
            .SingleAsync();

        Assert.True(keyCount > 0, "Expected at least one Data Protection key row in the database.");
    }

    private static async Task SeedEmployeeAsync(
        SqlServerDatabaseLease lease,
        string username,
        string pin
    )
    {
        await using var context = lease.CreateDbContext();
        await context.Database.MigrateAsync();
        context.Employees.Add(
            new Employee
            {
                Username = username,
                NormalizedUsername = username.ToUpperInvariant(),
                DisplayName = username,
                PinHash = new Pbkdf2PinHasher().Hash(pin),
                Role = EmployeeRole.Picker,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            }
        );
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Unlike <see cref="Auth.AuthWebApplicationFactory"/>, which leases a fresh database per
    /// instance, this points every instance at one connection string — which is the whole point
    /// here, since the key ring is what the two hosts must share.
    /// </summary>
    private sealed class SharedDatabaseFactory(string connectionString)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:LootSingles", connectionString);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<LootSinglesDbContext>>();
                services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<LootSinglesDbContext>>();
                services.AddDbContext<LootSinglesDbContext>(options =>
                    options.UseSqlServer(connectionString)
                );
            });
        }

        // The auth cookie is Secure, so the TestServer must believe the request is HTTPS or the
        // cookie is never sent back.
        public HttpClient CreateHttpsClient() =>
            CreateClient(
                new WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://localhost"),
                }
            );
    }
}
