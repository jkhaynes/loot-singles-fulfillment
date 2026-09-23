using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LootSingles.IntegrationTests.Hosting;

/// <summary>
/// 019 T004 / research.md §2. Azure Container Apps terminates TLS at its ingress and forwards plain
/// HTTP to the container. <c>UseHttpsRedirection()</c> then sees an insecure request and answers
/// with a redirect, which the ingress serves back over HTTPS, which redirects again — a loop that
/// makes the application unreachable in production while working perfectly on a developer machine.
///
/// These tests pin the fix: the forwarded protocol header must be honoured, so a request the
/// ingress already served over HTTPS is answered rather than redirected.
/// </summary>
public sealed class ForwardedHeadersTests
{
    // Without a configured HTTPS port, UseHttpsRedirection quietly does nothing in a test host,
    // which would make these tests pass for the wrong reason. Configuring one turns the redirect on.
    private const string HttpsPort = "443";

    [Fact]
    public async Task Request_forwarded_as_https_by_the_ingress_is_served_not_redirected()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var response = await client.GetAsync("/api/orders");

        Assert.NotEqual(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MovedPermanently, response.StatusCode);
        // Reaching the endpoint and being rejected for want of a session is the proof it was served.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_not_forwarded_as_https_still_redirects()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );

        var response = await client.GetAsync("/api/orders");

        // HTTPS redirection stays on. Trusting the forwarded header must not become "serve anything
        // over plain HTTP" — the session cookie is Secure, and this is the control that protects it.
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("https_port", HttpsPort);
            builder.UseSetting(
                "ConnectionStrings:LootSingles",
                "Server=forwarded-headers-test.invalid;Database=loot-singles;Encrypt=True"
            );
        });
}
