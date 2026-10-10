using System.Net;
using LootSingles.Application.Import;
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
    public async Task Api_client_is_a_typed_client_on_the_Tcgplayer_pipeline_with_a_shared_store_key_cache()
    {
        var stub = StubHttpMessageHandler.RespondingPerRequest(request => new HttpResponseMessage(
            HttpStatusCode.OK
        )
        {
            Content = new StringContent(
                request.RequestUri!.AbsolutePath == "/token"
                    ? """{"access_token":"synthetic-bearer-token-not-real","expires_in":1209600}"""
                    : """{"success":true,"errors":[],"results":[{"sellerKey":"SYNSTORE1"}]}"""
            ),
        });
        await using var provider = Build(stub);

        var first = provider.GetRequiredService<TcgplayerApiClient>();
        var second = provider.GetRequiredService<TcgplayerApiClient>();
        Assert.Equal("SYNSTORE1", await first.GetStoreKeyAsync(CancellationToken.None));
        Assert.Equal("SYNSTORE1", await second.GetStoreKeyAsync(CancellationToken.None));

        var api = Assert.Single(stub.Requests, r => r.RequestUri!.AbsolutePath != "/token");
        Assert.Equal(new Uri("https://api.tcgplayer.com/v1.39.0/stores/self"), api.RequestUri);
        Assert.Equal(TcgplayerUserAgent.Value, api.Headers.UserAgent.ToString());
        Assert.Equal("bearer", api.Headers.Authorization!.Scheme);
    }

    [Fact]
    public void Client_has_no_overall_timeout_because_each_attempt_is_bounded_inside_the_limiter()
    {
        // Ruling R15: a whole-call timeout would also count the time spent waiting for a
        // rate-limit slot, failing a large import instead of slowing it down (FR-021, SC-002).
        using var provider = Build(null);
        var client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(TcgplayerServiceCollectionExtensions.HttpClientName);

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    // ---- R15: the 30-second bound covers one HTTP attempt, not the wait for a slot ----

    private static readonly DateTimeOffset Start = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private const string ManifestBody =
        """{"results":[{"orderStatusTypes":[{"id":2,"name":"Ready To Ship"}]}]}""";

    private static ServiceProvider BuildWithClock(
        FakeTimeProvider time,
        HttpMessageHandler primary,
        params (string Key, string? Value)[] values
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddTcgplayer(Config([.. Secrets, ("Tcgplayer:StoreKey", "SYNSTORE1"), .. values]));
        services
            .AddHttpClient(TcgplayerServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => primary);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_request_waiting_longer_than_30_seconds_for_a_rate_limit_slot_still_succeeds()
    {
        var time = new FakeTimeProvider(Start);
        var upstream = new SlowUpstream(hangApiCalls: false, hangToken: false);
        await using var provider = BuildWithClock(
            time,
            upstream,
            ("Tcgplayer:CallsPerMinute", "2")
        );
        var client = provider.GetRequiredService<TcgplayerApiClient>();

        // The token request and the first manifest call use both slots of this minute.
        Assert.Equal([2], await client.GetOpenOrderStatusIdsAsync(CancellationToken.None));

        var waiting = client.GetOpenOrderStatusIdsAsync(CancellationToken.None);
        await SettleAsync(time, expectedTimers: 1);
        time.Advance(TimeSpan.FromSeconds(31));
        await SettleAsync(time, expectedTimers: 1);
        Assert.False(waiting.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(29));

        Assert.Equal([2], await waiting.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(3, provider.GetRequiredService<TcgplayerRateLimiter>().CallCount);
    }

    [Fact]
    public async Task An_attempt_TCGplayer_does_not_answer_within_30_seconds_is_Unavailable()
    {
        var time = new FakeTimeProvider(Start);
        var upstream = new SlowUpstream(hangApiCalls: true, hangToken: false);
        await using var provider = BuildWithClock(time, upstream);
        var client = provider.GetRequiredService<TcgplayerApiClient>();

        var call = client.GetOpenOrderStatusIdsAsync(CancellationToken.None);
        await SettleAsync(time, expectedTimers: 1);
        time.Advance(TimeSpan.FromSeconds(29));
        await SettleAsync(time, expectedTimers: 1);
        Assert.False(call.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(1));

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            call.WaitAsync(TimeSpan.FromSeconds(10))
        );
        Assert.Equal(TcgplayerFeedFailure.Unavailable, failure.Failure);
    }

    [Fact]
    public async Task A_token_request_TCGplayer_does_not_answer_within_30_seconds_is_Unavailable()
    {
        var time = new FakeTimeProvider(Start);
        var upstream = new SlowUpstream(hangApiCalls: false, hangToken: true);
        await using var provider = BuildWithClock(time, upstream);
        var client = provider.GetRequiredService<TcgplayerApiClient>();

        var call = client.GetOpenOrderStatusIdsAsync(CancellationToken.None);
        await SettleAsync(time, expectedTimers: 1);
        time.Advance(TimeSpan.FromSeconds(30));

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            call.WaitAsync(TimeSpan.FromSeconds(10))
        );
        Assert.Equal(TcgplayerFeedFailure.Unavailable, failure.Failure);
    }

    [Fact]
    public async Task Caller_cancellation_while_waiting_for_a_slot_is_OperationCanceledException()
    {
        var time = new FakeTimeProvider(Start);
        var upstream = new SlowUpstream(hangApiCalls: false, hangToken: false);
        await using var provider = BuildWithClock(
            time,
            upstream,
            ("Tcgplayer:CallsPerMinute", "2")
        );
        var client = provider.GetRequiredService<TcgplayerApiClient>();
        await client.GetOpenOrderStatusIdsAsync(CancellationToken.None);

        using var cancel = new CancellationTokenSource();
        var waiting = client.GetOpenOrderStatusIdsAsync(cancel.Token);
        await SettleAsync(time, expectedTimers: 1);
        time.Advance(TimeSpan.FromSeconds(31));
        await cancel.CancelAsync();

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            waiting.WaitAsync(TimeSpan.FromSeconds(10))
        );
        Assert.Equal(cancel.Token, thrown.CancellationToken);
        Assert.Equal(2, upstream.Requests);
    }

    /// <summary>
    /// Waits briefly, in real time, until the fake clock holds the expected number of pending
    /// timers, so an Advance sees a settled pipeline. The deadline only guards against a hang.
    /// </summary>
    private static async Task SettleAsync(FakeTimeProvider time, int expectedTimers)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (time.ActiveTimerCount != expectedTimers)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The pipeline did not settle on the fake clock.");
            }
            await Task.Delay(1);
        }
    }

    /// <summary>
    /// Synthetic upstream: answers the token request and the manifest, or never answers one of
    /// them until the request is cancelled.
    /// </summary>
    private sealed class SlowUpstream(bool hangApiCalls, bool hangToken) : HttpMessageHandler
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref _requests);
            var isToken = request.RequestUri!.AbsolutePath == "/token";
            if (isToken ? hangToken : hangApiCalls)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    isToken
                        ? """{"access_token":"synthetic-bearer-token-not-real","expires_in":1209600}"""
                        : ManifestBody
                ),
            };
        }
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
