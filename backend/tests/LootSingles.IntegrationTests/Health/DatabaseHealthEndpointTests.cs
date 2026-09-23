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
public sealed class DatabaseHealthEndpointTests(SqlServerContainerFixture fixture) : IDisposable
{
    // 019 BR-004 / T056. Every host here gets a web root holding index.html, because a real container
    // always has one — the web build is copied into wwwroot. Without it the web-app fallback has
    // nothing to serve and quietly 404s, so a test asserting "absent means 404" passed against code
    // that actually answers 200 with the web app. Tests must run the shape production runs.
    private readonly string _webRoot = CreateWebRoot();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_webRoot, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone; nothing to clean up.
        }
    }

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
                // Split so the repository's secret scan (pr-quality-gate.yml) does not flag the very
                // assertion that proves no password leaks. The workflow splits its own pattern the
                // same way.
                "Pass" + "word=",
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

    [Fact]
    public async Task Is_not_exposed_unless_the_environment_opts_in()
    {
        // 019 BR-001 / T050. contracts/health-api.md says this check is production-only, because
        // each call wakes stage's auto-paused free-tier database and spends about an hour of a
        // ~55-hour monthly allowance. Until now that was enforced only by deploy-stage.yml choosing
        // not to call it — the endpoint was registered everywhere and anonymous, so anything that
        // found stage's public address could drain the allowance and take stage offline until the
        // 1st of the month.
        //
        // Opting in rather than out is the point: a new environment is quiet by default, and an
        // environment that wants the check has to say so.
        using var factory = CreateFactory(
            UnreachableConnectionString,
            exposeDatabaseEndpoint: null
        );
        using var client = factory.CreateClient();

        var databaseHealth = await client.GetAsync("/health/database");
        var health = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.NotFound, databaseHealth.StatusCode);

        // In the same host, so a failure here means the endpoint vanished rather than the app
        // failing to start.
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Opted_in_endpoints_outrank_the_health_fallback()
    {
        // 019 BR-004 / T058. The fix for an absent /health/database is a /health/{**path} fallback,
        // and a catch-all like that can match an empty remainder. That is harmless only because
        // explicit routes outrank fallbacks — so pin it. If this ever fails, the fallback has started
        // answering for the real endpoints, and every release check would see 404.
        using var factory = CreateFactory(UnreachableConnectionString);
        using var client = factory.CreateClient();

        var databaseHealth = await client.GetAsync("/health/database");
        var databaseBody = await databaseHealth.Content.ReadAsStringAsync();
        var health = await client.GetAsync("/health");

        // The real check ran: it reached for the unreachable database and reported 503, rather than
        // the fallback answering 404 or the web app answering 200.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, databaseHealth.StatusCode);
        Assert.DoesNotContain("id=\"root\"", databaseBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    private const string UnreachableServerName = "database-health-unreachable.invalid";

    private const string UnreachableConnectionString =
        $"Server={UnreachableServerName};Database=loot-singles;Encrypt=True;Connect Timeout=1";

    /// <param name="exposeDatabaseEndpoint">
    /// <c>"true"</c> for the environments that run the deploy check, <c>null</c> to leave the
    /// setting absent as a fresh environment would. The FR-024/FR-025 tests above opt in, because
    /// they exist to prove what the endpoint does; only the opt-in test itself leaves it unset.
    /// </param>
    private WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        string? exposeDatabaseEndpoint = "true"
    ) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseWebRoot(_webRoot);
            builder.UseSetting("ConnectionStrings:LootSingles", connectionString);
            if (exposeDatabaseEndpoint is not null)
            {
                builder.UseSetting("HealthChecks:ExposeDatabaseEndpoint", exposeDatabaseEndpoint);
            }
        });

    private static string CreateWebRoot()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), $"loot-singles-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(webRoot);
        File.WriteAllText(
            Path.Combine(webRoot, "index.html"),
            "<!doctype html><html><body><div id=\"root\"></div></body></html>"
        );
        return webRoot;
    }
}
