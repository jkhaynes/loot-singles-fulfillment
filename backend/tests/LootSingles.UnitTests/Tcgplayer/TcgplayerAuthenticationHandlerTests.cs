using System.Net;
using LootSingles.Application.Import;
using LootSingles.Infrastructure.Tcgplayer;
using LootSingles.UnitTests.CardCatalog;
using Microsoft.Extensions.Logging.Abstractions;

namespace LootSingles.UnitTests.Tcgplayer;

/// <summary>
/// T020: every TCGplayer call carries a bearer token fetched with the store's existing
/// credentials (research.md §1; contracts/tcgplayer-upstream.md call #1). The token is cached
/// until 24 hours before <c>.expires</c>, a 401 refreshes it once, and the app never asks
/// TCGplayer to create an authorization. All values are synthetic.
/// </summary>
public sealed class TcgplayerAuthenticationHandlerTests
{
    // The synthetic fixture was issued at this instant and expires 14 days later.
    private static readonly DateTimeOffset Issued = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Expires = Issued.AddDays(14);

    private const string FixtureToken = "synthetic-bearer-token-not-real";
    private const string PublicId = "synthetic-public-id";
    private const string PrivateId = "synthetic-private-id";
    private const string StoreAccess = "synthetic-store-access";
    private const string ApiUrl = "https://api.tcgplayer.com/v1.39.0/stores/self";
    private const string TokenUrl = "https://api.tcgplayer.com/token";

    [Fact]
    public async Task First_call_posts_one_token_request_with_the_configured_credentials_and_sends_the_bearer_token()
    {
        var harness = new Harness();

        using var response = await harness.SendApiRequestAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = Assert.Single(harness.TokenRequests);
        Assert.Equal(HttpMethod.Post, token.Method);
        Assert.Equal(new Uri(TokenUrl), token.RequestUri);
        Assert.Equal(StoreAccess, Assert.Single(token.Headers.GetValues("X-Tcg-Access-Token")));
        Assert.Null(token.Headers.Authorization);
        Assert.Equal(
            $"grant_type=client_credentials&client_id={PublicId}&client_secret={PrivateId}",
            Assert.Single(harness.TokenForms)
        );

        var api = Assert.Single(harness.ApiRequests);
        Assert.Equal("bearer", api.Headers.Authorization!.Scheme);
        Assert.Equal(FixtureToken, api.Headers.Authorization.Parameter);
        harness.AssertNoAuthorizeRequest();
    }

    [Fact]
    public async Task Later_calls_reuse_the_cached_token_until_24_hours_before_it_expires()
    {
        var harness = new Harness();

        (await harness.SendApiRequestAsync()).Dispose();
        harness.Time.Advance(Expires - Issued - TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));
        (await harness.SendApiRequestAsync()).Dispose();
        Assert.Single(harness.TokenRequests);

        harness.Time.Advance(TimeSpan.FromSeconds(1)); // exactly 24 hours before .expires
        (await harness.SendApiRequestAsync()).Dispose();

        Assert.Equal(2, harness.TokenRequests.Count);
        Assert.Equal(3, harness.ApiRequests.Count);
        harness.AssertNoAuthorizeRequest();
    }

    [Fact]
    public async Task Concurrent_first_calls_share_one_token_request()
    {
        var gate = new TaskCompletionSource();
        var harness = new Harness(tokenGate: gate.Task);

        // Each call runs synchronously until it awaits, so every call after the first reaches the
        // token cache while the first call's token request is still in flight.
        var calls = Enumerable.Range(0, 5).Select(_ => harness.SendApiRequestAsync()).ToList();
        Assert.All(calls, call => Assert.False(call.IsCompleted));

        gate.SetResult();
        var responses = await Task.WhenAll(calls);

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Single(harness.TokenRequests);
        Assert.Equal(5, harness.ApiRequests.Count);
        Assert.All(
            harness.ApiRequests,
            api => Assert.Equal(FixtureToken, api.Headers.Authorization!.Parameter)
        );
        harness.AssertNoAuthorizeRequest();
    }

    [Fact]
    public async Task A_401_refreshes_the_token_once_and_retries_the_request_once_with_its_headers()
    {
        var harness = new Harness(apiStatuses: [HttpStatusCode.Unauthorized, HttpStatusCode.OK]);

        using var response = await harness.SendApiRequestAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, harness.TokenRequests.Count);
        Assert.Equal(2, harness.ApiRequests.Count);
        var retry = harness.ApiRequests[1];
        Assert.NotSame(harness.ApiRequests[0], retry);
        Assert.Equal(HttpMethod.Get, retry.Method);
        Assert.Equal(new Uri(ApiUrl), retry.RequestUri);
        Assert.Equal(FixtureToken + "-2", retry.Headers.Authorization!.Parameter);
        Assert.Equal("application/json", Assert.Single(retry.Headers.Accept).MediaType);
        harness.AssertNoAuthorizeRequest();
    }

    [Fact]
    public async Task A_second_401_after_the_refresh_is_access_refused_without_a_third_attempt()
    {
        var harness = new Harness(
            apiStatuses: [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized]
        );

        var exception = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.SendApiRequestAsync()
        );

        Assert.Equal(TcgplayerFeedFailure.AccessRefused, exception.Failure);
        Assert.Equal(2, harness.TokenRequests.Count);
        Assert.Equal(2, harness.ApiRequests.Count);
        AssertSafeMessage(exception);
        harness.AssertNoAuthorizeRequest();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_rejected_token_request_is_access_refused_and_no_api_call_is_sent(
        HttpStatusCode status
    )
    {
        var harness = new Harness(tokenStatus: status);

        var exception = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.SendApiRequestAsync()
        );

        Assert.Equal(TcgplayerFeedFailure.AccessRefused, exception.Failure);
        Assert.Single(harness.TokenRequests);
        Assert.Empty(harness.ApiRequests);
        AssertSafeMessage(exception);
        harness.AssertNoAuthorizeRequest();
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_token_request_that_fails_on_TCGplayers_side_is_unavailable(
        HttpStatusCode status
    )
    {
        var harness = new Harness(tokenStatus: status);

        var exception = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.SendApiRequestAsync()
        );

        Assert.Equal(TcgplayerFeedFailure.Unavailable, exception.Failure);
        Assert.Empty(harness.ApiRequests);
        AssertSafeMessage(exception);
    }

    [Fact]
    public async Task A_token_request_that_cannot_reach_TCGplayer_is_unavailable()
    {
        var stub = StubHttpMessageHandler.Throwing(new HttpRequestException("connection refused"));
        var cache = NewCache(new FakeTimeProvider(Issued));
        using var invoker = new HttpMessageInvoker(
            new TcgplayerAuthenticationHandler(cache, new TcgplayerOptions())
            {
                InnerHandler = stub,
            }
        );

        var exception = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, ApiUrl), default)
        );

        Assert.Equal(TcgplayerFeedFailure.Unavailable, exception.Failure);
        var only = Assert.Single(stub.Requests);
        Assert.Equal(new Uri(TokenUrl), only.RequestUri);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "token_type": "bearer", ".expires": "Fri, 23 Oct 2026 12:00:00 GMT" }""")]
    [InlineData("""{ "access_token": "synthetic-bearer-token-not-real" }""")]
    public async Task An_unusable_token_response_is_response_invalid(string body)
    {
        var harness = new Harness(tokenBody: body);

        var exception = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.SendApiRequestAsync()
        );

        Assert.Equal(TcgplayerFeedFailure.ResponseInvalid, exception.Failure);
        Assert.Empty(harness.ApiRequests);
        AssertSafeMessage(exception);
    }

    [Fact]
    public async Task Missing_credentials_are_not_configured_and_nothing_is_sent()
    {
        var stub = StubHttpMessageHandler.ReturningJson("{}");
        var cache = new TcgplayerTokenCache(
            new TcgplayerOptions(),
            new FakeTimeProvider(Issued),
            NullLogger<TcgplayerTokenCache>.Instance
        );
        using var invoker = new HttpMessageInvoker(
            new TcgplayerAuthenticationHandler(cache, new TcgplayerOptions())
            {
                InnerHandler = stub,
            }
        );

        var exception = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, ApiUrl), default)
        );

        Assert.Equal(TcgplayerFeedFailure.NotConfigured, exception.Failure);
        Assert.Empty(stub.Requests);
    }

    [Theory]
    [InlineData("https://elsewhere.example/v1.39.0/stores/self")]
    [InlineData("http://api.tcgplayer.com/v1.39.0/stores/self")]
    [InlineData("https://api.tcgplayer.com:8443/v1.39.0/stores/self")]
    public async Task A_request_to_any_other_scheme_host_or_port_passes_through_without_a_token(
        string url
    )
    {
        var stub = StubHttpMessageHandler.RespondingPerRequest(_ => new HttpResponseMessage(
            HttpStatusCode.Unauthorized
        ));
        using var invoker = new HttpMessageInvoker(
            new TcgplayerAuthenticationHandler(
                NewCache(new FakeTimeProvider(Issued)),
                new TcgplayerOptions()
            )
            {
                InnerHandler = stub,
            }
        );

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, url),
            default
        );

        // No token is fetched or attached, and a 401 is handed back rather than refreshed.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var only = Assert.Single(stub.Requests);
        Assert.Equal(new Uri(url), only.RequestUri);
        Assert.Null(only.Headers.Authorization);
    }

    [Fact]
    public async Task No_request_in_a_full_session_targets_app_authorize()
    {
        var harness = new Harness(
            apiStatuses: [HttpStatusCode.OK, HttpStatusCode.Unauthorized, HttpStatusCode.OK]
        );

        (await harness.SendApiRequestAsync()).Dispose();
        (await harness.SendApiRequestAsync()).Dispose();
        harness.Time.Advance(TimeSpan.FromDays(14));
        (await harness.SendApiRequestAsync()).Dispose();

        Assert.NotEmpty(harness.AllRequests);
        Assert.All(
            harness.AllRequests,
            request =>
                Assert.True(
                    (request.Method == HttpMethod.Post && request.RequestUri == new Uri(TokenUrl))
                        || (
                            request.Method == HttpMethod.Get
                            && request.RequestUri == new Uri(ApiUrl)
                        ),
                    $"Unexpected request {request.Method} {request.RequestUri}"
                )
        );
        harness.AssertNoAuthorizeRequest();
    }

    private static void AssertSafeMessage(TcgplayerFeedException exception)
    {
        foreach (var secret in new[] { FixtureToken, PublicId, PrivateId, StoreAccess, "marker" })
        {
            Assert.DoesNotContain(secret, exception.Message);
        }
    }

    private static TcgplayerTokenCache NewCache(TimeProvider time) =>
        new(
            new TcgplayerOptions
            {
                PublicKey = PublicId,
                PrivateKey = PrivateId,
                AccessToken = StoreAccess,
            },
            time,
            NullLogger<TcgplayerTokenCache>.Instance
        );

    private static string TokenJson(string token) =>
        File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tcgplayer", "token.json")
            )
            .Replace(FixtureToken, token);

    /// <summary>
    /// The auth handler over the shared <see cref="StubHttpMessageHandler"/>. Token requests get
    /// the synthetic fixture (a new token value each time); API requests get the next scripted
    /// status, then 200.
    /// </summary>
    private sealed class Harness
    {
        private readonly HttpMessageInvoker _invoker;
        private readonly StubHttpMessageHandler _stub;
        private readonly Queue<HttpStatusCode> _apiStatuses;
        private int _tokenCount;

        public Harness(
            HttpStatusCode[]? apiStatuses = null,
            HttpStatusCode tokenStatus = HttpStatusCode.OK,
            string? tokenBody = null,
            Task? tokenGate = null
        )
        {
            _apiStatuses = new Queue<HttpStatusCode>(apiStatuses ?? []);
            _stub = StubHttpMessageHandler.RespondingPerRequest(request =>
            {
                if (request.RequestUri!.AbsolutePath == "/token")
                {
                    TokenForms.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                    if (tokenStatus != HttpStatusCode.OK)
                    {
                        return new HttpResponseMessage(tokenStatus)
                        {
                            Content = new StringContent("synthetic-error-body-marker"),
                        };
                    }

                    _tokenCount++;
                    var value = _tokenCount == 1 ? FixtureToken : $"{FixtureToken}-{_tokenCount}";
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(tokenBody ?? TokenJson(value)),
                    };
                }

                var status = _apiStatuses.Count > 0 ? _apiStatuses.Dequeue() : HttpStatusCode.OK;
                return new HttpResponseMessage(status)
                {
                    Content = new StringContent("synthetic-api-body-marker"),
                };
            });

            HttpMessageHandler inner = tokenGate is null
                ? _stub
                : new SerializingGate(tokenGate) { InnerHandler = _stub };
            _invoker = new HttpMessageInvoker(
                new TcgplayerAuthenticationHandler(NewCache(Time), new TcgplayerOptions())
                {
                    InnerHandler = inner,
                }
            );
        }

        public FakeTimeProvider Time { get; } = new(Issued);

        public List<string> TokenForms { get; } = [];

        public List<HttpRequestMessage> AllRequests => _stub.Requests;

        public List<HttpRequestMessage> TokenRequests =>
            _stub.Requests.Where(r => r.RequestUri!.AbsolutePath == "/token").ToList();

        public List<HttpRequestMessage> ApiRequests =>
            _stub.Requests.Where(r => r.RequestUri!.AbsolutePath != "/token").ToList();

        public Task<HttpResponseMessage> SendApiRequestAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            request.Headers.Accept.ParseAdd("application/json");
            return _invoker.SendAsync(request, CancellationToken.None);
        }

        public void AssertNoAuthorizeRequest() =>
            Assert.DoesNotContain(
                _stub.Requests,
                r =>
                    r.RequestUri!.AbsolutePath.Contains(
                        "/app/authorize",
                        StringComparison.OrdinalIgnoreCase
                    )
            );
    }

    /// <summary>
    /// Holds token requests until the gate opens, and lets only one request at a time into the
    /// (non-thread-safe) stub so its request log stays exact.
    /// </summary>
    private sealed class SerializingGate(Task tokenGate) : DelegatingHandler
    {
        private readonly Lock _lock = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.RequestUri!.AbsolutePath == "/token")
            {
                await tokenGate;
            }

            Task<HttpResponseMessage> sent;
            lock (_lock)
            {
                sent = base.SendAsync(request, cancellationToken);
            }
            return await sent;
        }
    }
}
