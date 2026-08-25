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

    private static TcgdexCardCatalogProvider NewProvider(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.tcgdex.net/v2/en/"),
        };
        return new TcgdexCardCatalogProvider(httpClient);
    }
}
