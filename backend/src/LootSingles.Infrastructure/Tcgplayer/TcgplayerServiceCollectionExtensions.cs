using LootSingles.Application.Import;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LootSingles.Infrastructure.Tcgplayer;

/// <summary>
/// The one TCGplayer registration, called by both the API and the E2E host (plan.md, Structure
/// Decision) so a second copy of the pipeline can never drop the User-Agent or the rate limiter.
/// </summary>
public static class TcgplayerServiceCollectionExtensions
{
    /// <summary>Name of the TCGplayer <see cref="HttpClient"/>; resolve it from IHttpClientFactory.</summary>
    public const string HttpClientName = "Tcgplayer";

    /// <summary>
    /// Binds the <c>Tcgplayer</c> options (throws now on an invalid value), registers the shared
    /// limiter and token cache, builds the client pipeline and attaches
    /// <see cref="TcgplayerApiClient"/> to it as a typed client. With no secrets configured it still
    /// registers; <see cref="TcgplayerOptions.IsConfigured"/> is then false.
    /// </summary>
    public static IHttpClientBuilder AddTcgplayer(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var options = TcgplayerOptions.FromConfiguration(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton(sp => new TcgplayerRateLimiter(
            options.CallsPerMinute,
            sp.GetRequiredService<TimeProvider>()
        ));
        services.AddSingleton<TcgplayerTokenCache>();
        services.AddSingleton<TcgplayerStoreKeyCache>();
        services.AddTransient<TcgplayerAuthenticationHandler>();
        services.AddTransient<TcgplayerRateLimitHandler>();
        services.AddTransient<TcgplayerAttemptTimeoutHandler>();

        // Stateless over the transient typed client and the singleton options and limiter, so it
        // is transient too: the same lifetime as the client it composes.
        services.AddTransient<ITcgplayerOrderFeed, TcgplayerOrderFeed>();

        return services
            .AddHttpClient(
                HttpClientName,
                client =>
                {
                    // Relative request paths such as "stores/self" resolve under the API version;
                    // the token request builds its own absolute, unversioned URI from BaseUrl.
                    client.BaseAddress = new Uri(
                        new Uri(options.BaseUrl),
                        $"{options.ApiVersion}/"
                    );
                    // No whole-call timeout: it would count the wait for a rate-limit slot and
                    // fail a large import instead of slowing it down (ruling R15). Each attempt
                    // is bounded by TcgplayerAttemptTimeoutHandler instead.
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    client.DefaultRequestHeaders.TryAddWithoutValidation(
                        "User-Agent",
                        TcgplayerUserAgent.Value
                    );
                }
            )
            // The first handler added is outermost. Auth wraps the limiter, so the token fetch,
            // the call and the 401 retry each wait for a slot (ruling R9).
            .AddHttpMessageHandler<TcgplayerAuthenticationHandler>()
            .AddHttpMessageHandler<TcgplayerRateLimitHandler>()
            // Innermost: bounds each attempt (token request and 401 retry too) to 30 seconds
            // once it has its slot, so waiting for the limiter never times a request out.
            .AddHttpMessageHandler<TcgplayerAttemptTimeoutHandler>()
            // The API client is a transient typed client on this same pipeline; the store key it
            // resolves lives in the singleton TcgplayerStoreKeyCache.
            .AddTypedClient<TcgplayerApiClient>();
    }
}
