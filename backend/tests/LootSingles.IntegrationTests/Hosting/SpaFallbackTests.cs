using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LootSingles.IntegrationTests.Hosting;

/// <summary>
/// 019 T006 / contracts/health-api.md "Routing contract" / FR-004. One container serves both the API
/// and the built web application from a single origin, because the session cookie is
/// <c>SameSite=Strict</c> and cannot cross origins.
///
/// Fallback order is the requirement, not a convention. The web application needs unmatched paths to
/// return <c>index.html</c> so client-side routes deep-link — but if that fallback also catches
/// <c>/api/...</c>, a typo'd or removed API route answers 200 with HTML. A client expecting JSON
/// then fails on parse rather than on status, and a test asserting 404 passes for the wrong reason.
/// </summary>
public sealed class SpaFallbackTests : IDisposable
{
    private readonly string _webRoot;

    public SpaFallbackTests()
    {
        // A fixture web root, so these tests never depend on `npm run build` having been run.
        _webRoot = Path.Combine(Path.GetTempPath(), $"loot-singles-spa-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(
            Path.Combine(_webRoot, "index.html"),
            "<!doctype html><html><body><div id=\"root\"></div></body></html>"
        );
    }

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
    public async Task Root_serves_the_web_application()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"root\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_side_route_serves_the_web_application()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/orders/42");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"root\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unmatched_api_route_returns_not_found_rather_than_the_web_application()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/unknown");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("id=\"root\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unauthenticated_api_request_is_rejected_rather_than_served_the_web_application()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/orders");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("id=\"root\"", body, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseWebRoot(_webRoot);
            builder.UseSetting(
                "ConnectionStrings:LootSingles",
                "Server=spa-fallback-test.invalid;Database=loot-singles;Encrypt=True"
            );
        });
}
