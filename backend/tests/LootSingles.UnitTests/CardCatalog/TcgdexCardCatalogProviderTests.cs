using System.Net;
using LootSingles.Application.CardCatalog;
using LootSingles.Infrastructure.CardCatalog;

namespace LootSingles.UnitTests.CardCatalog;

public sealed class TcgdexCardCatalogProviderTests
{
    private static readonly CardIdentity Identity = new(
        "Genesect ex",
        "Black Bolt",
        "#67/086",
        null
    );

    private const string SetsListJson = """
        [
          { "id": "sv10.5b", "name": "Black Bolt" },
          { "id": "sv10.5w", "name": "White Flare" }
        ]
        """;

    [Fact]
    public async Task TryMatchImageUrlAsync_ExactSetAndNumberMatchWithVerifiedName_ReturnsImageUrl()
    {
        var provider = NewProvider(
            RouteByPath(
                ("/sets", SetsListJson),
                (
                    "/sets/sv10.5b/67",
                    """{ "name": "Genesect ex", "image": "https://assets.tcgdex.net/en/sv/sv10.5b/067" }"""
                )
            )
        );

        var result = await provider.TryMatchImageUrlAsync(Identity, CancellationToken.None);

        Assert.Equal("https://assets.tcgdex.net/en/sv/sv10.5b/067/high.webp", result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_NoCardAtThatNumber_ReturnsNull()
    {
        var provider = NewProvider(
            RouteByPath(("/sets", SetsListJson), ("/sets/sv10.5b/67", null))
        );

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
                    "/sets/sv10.5b/67",
                    """{ "name": "Some Other Card", "image": "https://assets.tcgdex.net/en/sv/sv10.5b/067" }"""
                )
            )
        );

        var result = await provider.TryMatchImageUrlAsync(Identity, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_SetHasKnownSeriesAbbreviationPrefix_ResolvesAgainstTheRealSetName()
    {
        var handler = RouteByPath(
            ("/sets", SetsListJson),
            (
                "/sets/sv10.5b/67",
                """{ "name": "Genesect ex", "image": "https://assets.tcgdex.net/en/sv/sv10.5b/067" }"""
            )
        );
        var provider = NewProvider(handler);

        var result = await provider.TryMatchImageUrlAsync(
            Identity with
            {
                Set = "SV: Black Bolt",
            },
            CancellationToken.None
        );

        Assert.Equal("https://assets.tcgdex.net/en/sv/sv10.5b/067/high.webp", result);
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
    public async Task TryMatchImageUrlAsync_CalledTwice_FetchesTheSetsListOnlyOnce()
    {
        var handler = RouteByPath(
            ("/sets", SetsListJson),
            (
                "/sets/sv10.5b/67",
                """{ "name": "Genesect ex", "image": "https://assets.tcgdex.net/en/sv/sv10.5b/067" }"""
            )
        );
        var provider = NewProvider(handler);

        await provider.TryMatchImageUrlAsync(Identity, CancellationToken.None);
        await provider.TryMatchImageUrlAsync(Identity, CancellationToken.None);

        Assert.Single(
            handler.Requests,
            request => request.RequestUri!.AbsolutePath.EndsWith("/sets")
        );
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_TwoProviderInstancesShareTheSameSetCatalog_FetchesTheSetsListOnlyOnce()
    {
        // The real scenario research.md §12 fixes: TcgdexCardCatalogProvider is Scoped (a fresh
        // instance per HTTP request), so this proves the sets list is shared across separate
        // provider instances via a common TcgdexSetCatalog, not just within one instance/request.
        var handler = RouteByPath(
            ("/sets", SetsListJson),
            (
                "/sets/sv10.5b/67",
                """{ "name": "Genesect ex", "image": "https://assets.tcgdex.net/en/sv/sv10.5b/067" }"""
            )
        );
        var setCatalog = NewSetCatalog(handler);
        var firstProvider = NewProvider(handler, setCatalog);
        var secondProvider = NewProvider(handler, setCatalog);

        await firstProvider.TryMatchImageUrlAsync(Identity, CancellationToken.None);
        await secondProvider.TryMatchImageUrlAsync(Identity, CancellationToken.None);

        Assert.Single(
            handler.Requests,
            request => request.RequestUri!.AbsolutePath.EndsWith("/sets")
        );
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_CollectorNumberHasLeadingZeros_QueriesTheApiWithoutThem()
    {
        var handler = RouteByPath(
            ("/sets", SetsListJson),
            (
                "/sets/sv10.5b/67",
                """{ "name": "Genesect ex", "image": "https://assets.tcgdex.net/en/sv/sv10.5b/067" }"""
            )
        );
        var provider = NewProvider(handler);

        await provider.TryMatchImageUrlAsync(
            Identity with
            {
                CollectorNumber = "#067/086",
            },
            CancellationToken.None
        );

        var cardRequest = handler.Requests.Single(request =>
            request.RequestUri!.AbsolutePath.Contains("/sets/sv10.5b/")
        );
        Assert.EndsWith("/67", cardRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_ReturnedNameDiffersOnlyByATrailingParenthetical_StillMatches()
    {
        var provider = NewProvider(
            RouteByPath(
                ("/sets", SetsListJson),
                (
                    "/sets/sv10.5b/67",
                    """{ "name": "Genesect ex", "image": "https://assets.tcgdex.net/en/sv/sv10.5b/067" }"""
                )
            )
        );

        var result = await provider.TryMatchImageUrlAsync(
            Identity with
            {
                ProductName = "Genesect ex (Full Art)",
            },
            CancellationToken.None
        );

        Assert.Equal("https://assets.tcgdex.net/en/sv/sv10.5b/067/high.webp", result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_SetNameNeedsTheColonToSpaceFallback_StillResolves()
    {
        var provider = NewProvider(
            RouteByPath(
                (
                    "/sets",
                    """
                    [{ "id": "cel25cc", "name": "Celebrations Classic Collection" }]
                    """
                ),
                (
                    "/sets/cel25cc/76",
                    """{ "name": "M Rayquaza EX", "image": "https://assets.tcgdex.net/en/swsh/cel25cc/076" }"""
                )
            )
        );

        var result = await provider.TryMatchImageUrlAsync(
            Identity with
            {
                Set = "Celebrations: Classic Collection",
                ProductName = "M Rayquaza EX",
                CollectorNumber = "#76/108",
            },
            CancellationToken.None
        );

        Assert.Equal("https://assets.tcgdex.net/en/swsh/cel25cc/076/high.webp", result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_PromoSetNameOnlyResolvesViaThePSuffixForm_StillResolves()
    {
        var provider = NewProvider(
            RouteByPath(
                (
                    "/sets",
                    """
                    [{ "id": "svp", "name": "SVP Black Star Promos" }]
                    """
                ),
                (
                    "/sets/svp/196",
                    """{ "name": "Charizard ex", "image": "https://assets.tcgdex.net/en/sv/svp/196" }"""
                )
            )
        );

        var result = await provider.TryMatchImageUrlAsync(
            Identity with
            {
                Set = "SV: Scarlet & Violet Promo Cards",
                ProductName = "Charizard ex",
                CollectorNumber = "#196",
            },
            CancellationToken.None
        );

        Assert.Equal("https://assets.tcgdex.net/en/sv/svp/196/high.webp", result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_PromoSetNameOnlyResolvesViaThePlainForm_StillResolves()
    {
        var provider = NewProvider(
            RouteByPath(
                (
                    "/sets",
                    """
                    [{ "id": "swshp", "name": "SWSH Black Star Promos" }]
                    """
                ),
                (
                    "/sets/swshp/54",
                    """{ "name": "Sobble", "image": "https://assets.tcgdex.net/en/swsh/swshp/SWSH054" }"""
                )
            )
        );

        var result = await provider.TryMatchImageUrlAsync(
            Identity with
            {
                Set = "SWSH: Sword & Shield Promo Cards",
                ProductName = "Sobble",
                CollectorNumber = "#054",
            },
            CancellationToken.None
        );

        Assert.Equal("https://assets.tcgdex.net/en/swsh/swshp/SWSH054/high.webp", result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_VariantClaimsHolofoilButCardIsNotHolo_ReturnsNull()
    {
        var provider = NewProvider(
            RouteByPath(
                ("/sets", SetsListJson),
                (
                    "/sets/sv10.5b/67",
                    """
                    {
                      "name": "Genesect ex",
                      "image": "https://assets.tcgdex.net/en/sv/sv10.5b/067",
                      "variants": { "holo": false, "reverse": false, "firstEdition": false }
                    }
                    """
                )
            )
        );

        var result = await provider.TryMatchImageUrlAsync(
            Identity with
            {
                Variant = "Holofoil",
            },
            CancellationToken.None
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_VariantClaimsHolofoilAndCardIsHolo_ReturnsImageUrl()
    {
        var provider = NewProvider(
            RouteByPath(
                ("/sets", SetsListJson),
                (
                    "/sets/sv10.5b/67",
                    """
                    {
                      "name": "Genesect ex",
                      "image": "https://assets.tcgdex.net/en/sv/sv10.5b/067",
                      "variants": { "holo": true, "reverse": false, "firstEdition": false }
                    }
                    """
                )
            )
        );

        var result = await provider.TryMatchImageUrlAsync(
            Identity with
            {
                Variant = "Holofoil",
            },
            CancellationToken.None
        );

        Assert.Equal("https://assets.tcgdex.net/en/sv/sv10.5b/067/high.webp", result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_VariantClaimsReverseHolofoilButCardIsNotReverse_ReturnsNull()
    {
        var provider = NewProvider(
            RouteByPath(
                ("/sets", SetsListJson),
                (
                    "/sets/sv10.5b/67",
                    """
                    {
                      "name": "Genesect ex",
                      "image": "https://assets.tcgdex.net/en/sv/sv10.5b/067",
                      "variants": { "holo": true, "reverse": false, "firstEdition": false }
                    }
                    """
                )
            )
        );

        var result = await provider.TryMatchImageUrlAsync(
            Identity with
            {
                Variant = "Reverse Holofoil",
            },
            CancellationToken.None
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_VariantClaims1stEditionButCardIsNot_ReturnsNull()
    {
        var provider = NewProvider(
            RouteByPath(
                ("/sets", SetsListJson),
                (
                    "/sets/sv10.5b/67",
                    """
                    {
                      "name": "Genesect ex",
                      "image": "https://assets.tcgdex.net/en/sv/sv10.5b/067",
                      "variants": { "holo": true, "reverse": false, "firstEdition": false }
                    }
                    """
                )
            )
        );

        var result = await provider.TryMatchImageUrlAsync(
            Identity with
            {
                Variant = "1st Edition",
            },
            CancellationToken.None
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_NoVariantClaim_IgnoresVariantsFieldEvenWhenHoloOnlyExclusive()
    {
        // Asymmetric by design (research.md): only reject when the packing slip's Variant text
        // explicitly claims a finish the provider says is impossible. A silent/absent Variant
        // never triggers a rejection, even against an exclusive-holo card, since we have no
        // confirmed evidence TCGplayer always labels every holo-exclusive product consistently -
        // a false rejection here would regress FR-001 for no confirmed safety benefit.
        var provider = NewProvider(
            RouteByPath(
                ("/sets", SetsListJson),
                (
                    "/sets/sv10.5b/67",
                    """
                    {
                      "name": "Genesect ex",
                      "image": "https://assets.tcgdex.net/en/sv/sv10.5b/067",
                      "variants": { "holo": true, "normal": false, "reverse": false, "firstEdition": false }
                    }
                    """
                )
            )
        );

        var result = await provider.TryMatchImageUrlAsync(Identity, CancellationToken.None);

        Assert.Equal("https://assets.tcgdex.net/en/sv/sv10.5b/067/high.webp", result);
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

    private static TcgdexCardCatalogProvider NewProvider(StubHttpMessageHandler handler) =>
        NewProvider(handler, NewSetCatalog(handler));

    private static TcgdexCardCatalogProvider NewProvider(
        StubHttpMessageHandler handler,
        TcgdexSetCatalog setCatalog
    )
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.tcgdex.net/v2/en/"),
        };
        return new TcgdexCardCatalogProvider(httpClient, setCatalog);
    }

    private static TcgdexSetCatalog NewSetCatalog(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.tcgdex.net/v2/en/"),
        };
        return new TcgdexSetCatalog(
            new SingleClientHttpClientFactory(httpClient),
            new FakeTimeProvider(DateTimeOffset.UtcNow)
        );
    }
}
