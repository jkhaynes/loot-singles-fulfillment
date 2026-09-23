using System.Net;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LootSingles.IntegrationTests.Health;

/// <summary>
/// 019 T047 / contracts/health-api.md / FR-024, FR-025, SC-007.
///
/// <c>/health/database</c> exists because a deployment check that only calls <c>/health</c> proves
/// the process started — which a container that cannot reach its database also does. Since the
/// application identity and the migration identity are separate (research.md §7), a successful
/// migration no longer implies the *application* can read anything. This endpoint is the only thing
/// that proves it, and a production release is not called successful without it.
///
/// The container probe must never use this endpoint. That is <c>/health</c>, and the asymmetry is
/// deliberate — see <see cref="HealthEndpointTests"/>.
/// </summary>
[Collection(SqlServerTestCollection.Name)]
public sealed class DatabaseHealthEndpointTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task Returns_ok_when_the_database_is_reachable()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        using var factory = CreateFactory(lease.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/database");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Returns_service_unavailable_when_the_database_is_unreachable()
    {
        using var factory = CreateFactory(UnreachableConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/database");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Health_stays_ok_in_the_same_run_where_database_health_fails()
    {
        // The deliberate difference between the two endpoints, pinned. If these ever agree, either
        // the probe has started depending on the database (FR-023) or the deploy check has stopped
        // proving anything the probe did not already prove (FR-024).
        using var factory = CreateFactory(UnreachableConnectionString);
        using var client = factory.CreateClient();

        var databaseHealth = await client.GetAsync("/health/database");
        var health = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, databaseHealth.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Failure_body_reveals_nothing_about_the_connection()
    {
        using var factory = CreateFactory(UnreachableConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/database");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        foreach (
            var leak in new[]
            {
                "Server=",
                "Database=",
                "User Id=",
                "Password=",
                UnreachableServerName,
                "SqlException",
                "Microsoft.Data.SqlClient",
                "at LootSingles",
            }
        )
        {
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    private const string UnreachableServerName = "database-health-unreachable.invalid";

    private const string UnreachableConnectionString =
        $"Server={UnreachableServerName};Database=loot-singles;Encrypt=True;Connect Timeout=1";

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:LootSingles", connectionString);
        });
}
