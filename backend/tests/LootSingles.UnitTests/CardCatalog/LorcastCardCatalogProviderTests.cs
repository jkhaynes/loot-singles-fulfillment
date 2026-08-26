using System.Net;
using LootSingles.Application.CardCatalog;
using LootSingles.Infrastructure.CardCatalog;
using Microsoft.Extensions.Logging.Abstractions;

namespace LootSingles.UnitTests.CardCatalog;

public sealed class LorcastCardCatalogProviderTests
{
    private static readonly CardIdentity Identity = new("Elsa", "The First Chapter", "#207", null);

    private const string SetsListJson = """
        { "results": [{ "id": "set_abc", "code": "1", "name": "The First Chapter" }] }
        """;

    [Fact]
    public async Task TryMatchImageUrlAsync_ExactSetAndNumberMatchWithVerifiedName_ReturnsImageUrl()
    {
        var provider = NewProvider(
            RouteByPath(
                ("/sets", SetsListJson),
                (
                    "/cards/1/207",
                    """
                    {
                      "name": "Elsa",
                      "image_uris": { "digital": { "large": "https://cards.lorcast.io/large/elsa.avif" } }
                    }
                    """
                )
            )
        );

        var result = await provider.TryMatchImageUrlAsync(Identity, CancellationToken.None);

        Assert.Equal("https://cards.lorcast.io/large/elsa.avif", result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_NoCardAtThatNumber_ReturnsNull()
    {
        var provider = NewProvider(RouteByPath(("/sets", SetsListJson), ("/cards/1/207", null)));

        var result = await provider.TryMatchImageUrlAsync(Identity, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_ReturnedCardNameDoesNotMatchImportedProductName_ReturnsNull()
    {
        var provider = NewProvider(
            RouteByPath(
                ("/sets", SetsListJson),
                (
                    "/cards/1/207",
                    """
                    {
                      "name": "Some Other Card",
                      "image_uris": { "digital": { "large": "https://cards.lorcast.io/large/other.avif" } }
                    }
                    """
                )
            )
        );

        var result = await provider.TryMatchImageUrlAsync(Identity, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_SetNameNotFoundInSetsList_ReturnsNullWithoutQueryingForACard()
    {
        var handler = RouteByPath(("/sets", SetsListJson));
        var provider = NewProvider(handler);

        var result = await provider.TryMatchImageUrlAsync(
            Identity with
            {
                Set = "Some Unknown Set",
            },
            CancellationToken.None
        );

        Assert.Null(result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_PromoCollectorNumberHasNoTotalSuffix_QueriesTheApiDirectly()
    {
        var handler = RouteByPath(
            ("/sets", SetsListJson),
            (
                "/cards/1/36",
                """
                {
                  "name": "Elsa",
                  "image_uris": { "digital": { "large": "https://cards.lorcast.io/large/elsa.avif" } }
                }
                """
            )
        );
        var provider = NewProvider(handler);

        var result = await provider.TryMatchImageUrlAsync(
            Identity with
            {
                CollectorNumber = "#36",
            },
            CancellationToken.None
        );

        Assert.Equal("https://cards.lorcast.io/large/elsa.avif", result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_CardHasASeparateVersionField_ComparesTheCombinedName()
    {
        // Confirmed live: Lorcast splits every card's printed name into separate "name" and
        // "version" fields (e.g. "Scrooge McDuck" / "S.H.U.S.H. Agent"), but TCGplayer's
        // ProductName is the combined "Name - Version" form - confirmed by the real fixture
        // OrderLineExtractionTests.cs already checks in ("Scrooge McDuck - S.H.U.S.H. Agent").
        var provider = NewProvider(
            RouteByPath(
                ("/sets", SetsListJson),
                (
                    "/cards/1/207",
                    """
                    {
                      "name": "Elsa",
                      "version": "Spirit of Winter",
                      "image_uris": { "digital": { "large": "https://cards.lorcast.io/large/elsa.avif" } }
                    }
                    """
                )
            )
        );

        var result = await provider.TryMatchImageUrlAsync(
            Identity with
            {
                ProductName = "Elsa - Spirit of Winter",
            },
            CancellationToken.None
        );

        Assert.Equal("https://cards.lorcast.io/large/elsa.avif", result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_SetNameHasMultipleCandidateCodes_ResolvesTheSoleValidMatch()
    {
        // Confirmed live: TCGplayer's "Disney Lorcana Promo Cards" label collapses several real
        // Lorcast promo sets that reuse the same collector numbers for different cards - the base
        // number "36" exists in three different promo sets, only one of which is this real card.
        var handler = RouteByPath(
            (
                "/sets",
                """
                {
                  "results": [
                    { "id": "set_p1", "code": "P1", "name": "Promo Set 1" },
                    { "id": "set_p2", "code": "P2", "name": "Promo Set 2" },
                    { "id": "set_p3", "code": "P3", "name": "Promo Set 3" }
                  ]
                }
                """
            ),
            (
                "/cards/P1/36",
                """{ "name": "Hidden Inkcaster", "image_uris": { "digital": { "large": "https://cards.lorcast.io/large/wrong.avif" } } }"""
            ),
            (
                "/cards/P2/36",
                """{ "name": "Mickey Mouse", "version": "True Friend", "image_uris": { "digital": { "large": "https://cards.lorcast.io/large/also-wrong.avif" } } }"""
            ),
            (
                "/cards/P3/36",
                """{ "name": "Scrooge McDuck", "version": "S.H.U.S.H. Agent", "image_uris": { "digital": { "large": "https://cards.lorcast.io/large/scrooge.avif" } } }"""
            )
        );
        var provider = NewProvider(handler);

        var result = await provider.TryMatchImageUrlAsync(
            new CardIdentity(
                "Scrooge McDuck - S.H.U.S.H. Agent",
                "Disney Lorcana Promo Cards",
                "#36",
                "Holofoil"
            ),
            CancellationToken.None
        );

        Assert.Equal("https://cards.lorcast.io/large/scrooge.avif", result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_MultipleCandidateCodesBothMatchTheName_ReturnsNull()
    {
        var handler = RouteByPath(
            (
                "/sets",
                """
                {
                  "results": [
                    { "id": "set_p1", "code": "P1", "name": "Promo Set 1" },
                    { "id": "set_p2", "code": "P2", "name": "Promo Set 2" }
                  ]
                }
                """
            ),
            (
                "/cards/P1/1",
                """{ "name": "Same Card", "image_uris": { "digital": { "large": "https://cards.lorcast.io/large/a.avif" } } }"""
            ),
            (
                "/cards/P2/1",
                """{ "name": "Same Card", "image_uris": { "digital": { "large": "https://cards.lorcast.io/large/b.avif" } } }"""
            )
        );
        var provider = NewProvider(handler);

        var result = await provider.TryMatchImageUrlAsync(
            new CardIdentity("Same Card", "Disney Lorcana Promo Cards", "#1", null),
            CancellationToken.None
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_ReturnedNameDiffersOnlyByATrailingParenthetical_StillMatches()
    {
        var provider = NewProvider(
            RouteByPath(
                ("/sets", SetsListJson),
                (
                    "/cards/1/207",
                    """
                    {
                      "name": "Elsa",
                      "image_uris": { "digital": { "large": "https://cards.lorcast.io/large/elsa.avif" } }
                    }
                    """
                )
            )
        );

        var result = await provider.TryMatchImageUrlAsync(
            Identity with
            {
                ProductName = "Elsa (Enchanted)",
            },
            CancellationToken.None
        );

        Assert.Equal("https://cards.lorcast.io/large/elsa.avif", result);
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_BoundsConcurrentCardRequestsWithASemaphore()
    {
        var handler = new ConcurrencyTrackingHandler(SetsListJson);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.lorcast.com/v0/"),
        };
        var setCatalog = new LorcastSetCatalog(
            new SingleClientHttpClientFactory(httpClient),
            new FakeTimeProvider(DateTimeOffset.UtcNow)
        );
        var provider = new LorcastCardCatalogProvider(
            httpClient,
            setCatalog,
            NullLogger<LorcastCardCatalogProvider>.Instance
        );
        var identities = Enumerable
            .Range(1, 12)
            .Select(i => new CardIdentity($"Card {i}", "The First Chapter", $"#{i}", null))
            .ToArray();

        await provider.TryMatchImageUrlsAsync(identities, CancellationToken.None);

        Assert.True(
            handler.MaxConcurrent <= 5,
            $"Expected concurrency to stay within the configured cap, but observed {handler.MaxConcurrent}."
        );
        Assert.True(
            handler.MaxConcurrent >= 2,
            $"Expected some real concurrency (not fully serialized), but observed {handler.MaxConcurrent}."
        );
    }

    /// <summary>
    /// A genuinely async <see cref="HttpMessageHandler"/> (unlike <see cref="StubHttpMessageHandler"/>,
    /// which completes synchronously and so never lets <c>Task.WhenAll</c> callers actually
    /// interleave) so this test can observe real concurrency through the semaphore gate.
    /// </summary>
    private sealed class ConcurrencyTrackingHandler(string setsListJson) : HttpMessageHandler
    {
        private readonly Lock _lock = new();
        private int _current;

        public int MaxConcurrent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/sets", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(setsListJson),
                };
            }

            var value = Interlocked.Increment(ref _current);
            lock (_lock)
            {
                MaxConcurrent = Math.Max(MaxConcurrent, value);
            }
            await Task.Delay(20, cancellationToken);
            Interlocked.Decrement(ref _current);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_OneIdentityThrowsThroughTheSemaphore_SiblingIdentityStillResolves()
    {
        var handler = StubHttpMessageHandler.RespondingPerRequest(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/sets", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SetsListJson),
                };
            }

            if (path.EndsWith("/cards/1/1", StringComparison.Ordinal))
            {
                throw new HttpRequestException("Simulated Lorcast failure.");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "name": "Card 2",
                      "image_uris": { "digital": { "large": "https://cards.lorcast.io/large/card-2.avif" } }
                    }
                    """
                ),
            };
        });
        var provider = NewProvider(handler);
        var failing = new CardIdentity("Card 1", "The First Chapter", "#1", null);
        var succeeding = new CardIdentity("Card 2", "The First Chapter", "#2", null);

        var results = await provider.TryMatchImageUrlsAsync(
            [failing, succeeding],
            CancellationToken.None
        );

        Assert.Null(results[failing]);
        Assert.Equal("https://cards.lorcast.io/large/card-2.avif", results[succeeding]);
    }

    private static StubHttpMessageHandler RouteByPath(
        params (string PathSuffix, string? Json)[] routes
    ) =>
        StubHttpMessageHandler.RespondingPerRequest(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            foreach (var (pathSuffix, json) in routes)
            {
                if (!path.EndsWith(pathSuffix, StringComparison.Ordinal))
                {
                    continue;
                }

                return json is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json),
                    };
            }

            throw new InvalidOperationException($"No stubbed route for '{path}'.");
        });

    private static LorcastCardCatalogProvider NewProvider(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.lorcast.com/v0/"),
        };
        var setCatalog = new LorcastSetCatalog(
            new SingleClientHttpClientFactory(httpClient),
            new FakeTimeProvider(DateTimeOffset.UtcNow)
        );
        return new LorcastCardCatalogProvider(
            httpClient,
            setCatalog,
            NullLogger<LorcastCardCatalogProvider>.Instance
        );
    }
}
