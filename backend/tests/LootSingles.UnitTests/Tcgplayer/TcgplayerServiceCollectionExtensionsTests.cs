using System.Net;
using LootSingles.Infrastructure.Tcgplayer;
using LootSingles.UnitTests.CardCatalog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LootSingles.UnitTests.Tcgplayer;

/// <summary>
/// T024: <c>AddTcgplayer</c> is the one registration the API and the E2E host share, so these tests
/// prove what the TCGplayer API agreement needs from it: every request, the token request
/// included, carries the User-Agent, passes the shared rate limiter, and gets the bearer header.
/// All values are synthetic.
/// </summary>
public sealed class TcgplayerServiceCollectionExtensionsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))
            )
            .Build();

    private static readonly (string, string?)[] Secrets =
    [
        ("Tcgplayer:PublicKey", "synthetic-public-id"),
        ("Tcgplayer:PrivateKey", "synthetic-private-id"),
        ("Tcgplayer:AccessToken", "synthetic-store-access"),
    ];

    private static ServiceProvider Build(
        StubHttpMessageHandler? primary,
        params (string Key, string? Value)[] values
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTcgplayer(Config([.. Secrets, .. values]));
        if (primary is not null)
        {
            services
                .AddHttpClient(TcgplayerServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => primary);
        }

        return services.BuildServiceProvider();
    }

    private static StubHttpMessageHandler Upstream() =>
        StubHttpMessageHandler.RespondingPerRequest(request => new HttpResponseMessage(
            HttpStatusCode.OK
        )
        {
            Content = new StringContent(
                request.RequestUri!.AbsolutePath == "/token"
                    ? """{"access_token":"synthetic-bearer-token-not-real","expires_in":1209600}"""
                    : "{}"
            ),
        });

    [Fact]
    public async Task Api_request_carries_the_user_agent_and_bearer_token_and_the_token_request_is_counted_by_the_limiter()
    {
        var stub = Upstream();
        await using var provider = Build(stub);
        var client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(TcgplayerServiceCollectionExtensions.HttpClientName);

        using var response = await client.GetAsync("stores/self");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = Assert.Single(stub.Requests, r => r.RequestUri!.AbsolutePath == "/token");
        var api = Assert.Single(stub.Requests, r => r.RequestUri!.AbsolutePath != "/token");
        Assert.Equal(new Uri("https://api.tcgplayer.com/v1.39.0/stores/self"), api.RequestUri);
        Assert.Equal("bearer", api.Headers.Authorization!.Scheme);
        Assert.Equal("synthetic-bearer-token-not-real", api.Headers.Authorization.Parameter);
        Assert.Equal(TcgplayerUserAgent.Value, api.Headers.UserAgent.ToString());
        Assert.Equal(TcgplayerUserAgent.Value, token.Headers.UserAgent.ToString());
        // Auth is the outer handler and the limiter the inner one, so the token request and the
        // API call each take a slot.
        Assert.Equal(2, provider.GetRequiredService<TcgplayerRateLimiter>().CallCount);
    }

    [Fact]
    public void Client_has_a_30_second_timeout()
    {
        using var provider = Build(null);
        var client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(TcgplayerServiceCollectionExtensions.HttpClientName);

        Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
    }

    [Fact]
    public void Options_limiter_and_token_cache_are_singletons()
    {
        using var provider = Build(null, ("Tcgplayer:CallsPerMinute", "90"));

        Assert.Equal(90, provider.GetRequiredService<TcgplayerOptions>().CallsPerMinute);
        Assert.Same(
            provider.GetRequiredService<TcgplayerRateLimiter>(),
            provider.GetRequiredService<TcgplayerRateLimiter>()
        );
        Assert.Same(
            provider.GetRequiredService<TcgplayerTokenCache>(),
            provider.GetRequiredService<TcgplayerTokenCache>()
        );
    }

    [Fact]
    public void No_secrets_still_registers_and_options_report_not_configured()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTcgplayer(Config());
        using var provider = services.BuildServiceProvider();

        Assert.False(provider.GetRequiredService<TcgplayerOptions>().IsConfigured);
    }

    [Fact]
    public void Calls_per_minute_above_the_cap_fails_at_registration()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() =>
            services.AddTcgplayer(Config(("Tcgplayer:CallsPerMinute", "151")))
        );
    }
}
