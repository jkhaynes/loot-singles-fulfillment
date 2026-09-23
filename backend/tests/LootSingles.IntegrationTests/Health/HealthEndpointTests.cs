using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LootSingles.IntegrationTests.Health;

/// <summary>
/// 019 T005 / contracts/health-api.md. <c>/health</c> is the container platform's liveness probe and
/// answers exactly one question: is this process up and serving HTTP?
///
/// It must never touch the database. The platform's response to a failing probe is to replace the
/// container, and restarting an application cannot fix a database problem — it only destroys a
/// working application that could still serve its sign-in page and log a useful error. FR-023.
/// </summary>
public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Health_returns_ok_without_authentication()
    {
        using var factory = CreateFactory(ReachableLookingConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_returns_ok_even_when_the_database_is_unreachable()
    {
        // The whole point of the endpoint. If this ever fails, a database outage becomes a
        // container-replacement loop and the shop loses an application that was still partly usable.
        using var factory = CreateFactory(UnreachableConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_body_carries_no_detail()
    {
        using var factory = CreateFactory(ReachableLookingConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        // Asserting the status too, not just the empty body: a 404 also has an empty body, so
        // without this the test would pass against an application that has no /health at all.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            string.IsNullOrWhiteSpace(body),
            $"Expected an empty body from /health but received: {body}"
        );
    }

    private const string ReachableLookingConnectionString =
        "Server=health-test.invalid;Database=loot-singles;Encrypt=True";

    private const string UnreachableConnectionString =
        "Server=health-unreachable.invalid;Database=loot-singles;Encrypt=True;Connect Timeout=1";

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:LootSingles", connectionString);
        });
}
