using System.Net;
using LootSingles.Application.CardCatalog;
using LootSingles.Infrastructure.CardCatalog;

namespace LootSingles.UnitTests.CardCatalog;

public sealed class ScryfallCardCatalogProviderTests
{
    private const string SetsListJson = """
        {
          "object": "list",
          "data": [
            { "code": "hob", "name": "The Hobbit" },
            { "code": "fic", "name": "Final Fantasy Commander" }
          ]
        }
        """;

    [Fact]
    public async Task TryMatchImageUrlsAsync_ExactMatchesWithVerifiedNames_ReturnsEachCardsImageUrl()
    {
        var handler = RouteByMethodAndPath(
            (HttpMethod.Get, "/sets", SetsListJson),
            (
                HttpMethod.Post,
                "/cards/collection",
                """
                {
                  "object": "list",
                  "not_found": [],
                  "data": [
                    { "name": "My Precious", "set": "hob", "collector_number": "271", "image_uris": { "large": "https://cards.scryfall.io/large/my-precious.jpg" } }
                  ]
                }
                """
            )
        );
        var provider = NewProvider(handler);
        CardIdentity[] identities = [new("My Precious", "The Hobbit", "#271", null)];

        var results = await provider.TryMatchImageUrlsAsync(identities, CancellationToken.None);

        Assert.Equal("https://cards.scryfall.io/large/my-precious.jpg", results[identities[0]]);
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_CardNotFoundInResponse_MapsToNull()
    {
        var handler = RouteByMethodAndPath(
            (HttpMethod.Get, "/sets", SetsListJson),
            (
                HttpMethod.Post,
                "/cards/collection",
                """{ "object": "list", "not_found": [{ "set": "hob", "collector_number": "271" }], "data": [] }"""
            )
        );
        var provider = NewProvider(handler);
        CardIdentity[] identities = [new("My Precious", "The Hobbit", "#271", null)];

        var results = await provider.TryMatchImageUrlsAsync(identities, CancellationToken.None);

        Assert.Null(results[identities[0]]);
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_ReturnedCardNameDoesNotMatch_MapsToNull()
    {
        var handler = RouteByMethodAndPath(
            (HttpMethod.Get, "/sets", SetsListJson),
            (
                HttpMethod.Post,
                "/cards/collection",
                """
                {
                  "object": "list",
                  "not_found": [],
                  "data": [
                    { "name": "Some Other Card", "set": "hob", "collector_number": "271", "image_uris": { "large": "https://cards.scryfall.io/large/other.jpg" } }
                  ]
                }
                """
            )
        );
        var provider = NewProvider(handler);
        CardIdentity[] identities = [new("My Precious", "The Hobbit", "#271", null)];

        var results = await provider.TryMatchImageUrlsAsync(identities, CancellationToken.None);

        Assert.Null(results[identities[0]]);
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_UnresolvableSetName_MapsToNullWithoutMakingACollectionRequest()
    {
        var handler = RouteByMethodAndPath((HttpMethod.Get, "/sets", SetsListJson));
        var provider = NewProvider(handler);
        CardIdentity[] identities = [new("Nonexistent", "Some Unknown Set", "#1", null)];

        var results = await provider.TryMatchImageUrlsAsync(identities, CancellationToken.None);

        Assert.Null(results[identities[0]]);
        Assert.DoesNotContain(
            handler.Requests,
            request => request.RequestUri!.AbsolutePath.EndsWith("/cards/collection")
        );
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_SetNameNeedsANormalizerCandidate_StillResolves()
    {
        var handler = RouteByMethodAndPath(
            (HttpMethod.Get, "/sets", SetsListJson),
            (
                HttpMethod.Post,
                "/cards/collection",
                """
                {
                  "object": "list",
                  "not_found": [],
                  "data": [
                    { "name": "Y'shtola, Night's Blessed", "set": "fic", "collector_number": "7", "image_uris": { "large": "https://cards.scryfall.io/large/yshtola.jpg" } }
                  ]
                }
                """
            )
        );
        var provider = NewProvider(handler);
        CardIdentity[] identities =
        [
            new("Y'shtola, Night's Blessed", "Commander: FINAL FANTASY", "#7", null),
        ];

        var results = await provider.TryMatchImageUrlsAsync(identities, CancellationToken.None);

        Assert.Equal("https://cards.scryfall.io/large/yshtola.jpg", results[identities[0]]);
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_MultiFacedCard_ComparesAgainstTheFirstFacesNameNotTheCombinedName()
    {
        // Confirmed live: an adventure/split/transform-layout card's top-level "name" is the
        // combined name (e.g. "My Precious // Allure of Power"), but the packing slip's
        // ProductName is only the front face ("My Precious"). The top-level image_uris is still
        // used for the image (confirmed live it's present even when card_faces lack their own).
        var handler = RouteByMethodAndPath(
            (HttpMethod.Get, "/sets", SetsListJson),
            (
                HttpMethod.Post,
                "/cards/collection",
                """
                {
                  "object": "list",
                  "not_found": [],
                  "data": [
                    {
                      "name": "My Precious // Allure of Power",
                      "set": "hob",
                      "collector_number": "271",
                      "image_uris": { "large": "https://cards.scryfall.io/large/my-precious.jpg" },
                      "card_faces": [
                        { "name": "My Precious" },
                        { "name": "Allure of Power" }
                      ]
                    }
                  ]
                }
                """
            )
        );
        var provider = NewProvider(handler);
        CardIdentity[] identities = [new("My Precious", "The Hobbit", "#271", null)];

        var results = await provider.TryMatchImageUrlsAsync(identities, CancellationToken.None);

        Assert.Equal("https://cards.scryfall.io/large/my-precious.jpg", results[identities[0]]);
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_BatchLargerThan75_SplitsIntoMultipleChunkedRequests()
    {
        var handler = RouteByMethodAndPath(
            (HttpMethod.Get, "/sets", SetsListJson),
            (
                HttpMethod.Post,
                "/cards/collection",
                """{ "object": "list", "not_found": [], "data": [] }"""
            )
        );
        var provider = NewProvider(handler);
        var identities = Enumerable
            .Range(1, 80)
            .Select(i => new CardIdentity($"Card {i}", "The Hobbit", $"#{i}", null))
            .ToArray();

        await provider.TryMatchImageUrlsAsync(identities, CancellationToken.None);

        var collectionRequests = handler
            .Requests.Where(request =>
                request.RequestUri!.AbsolutePath.EndsWith("/cards/collection")
            )
            .ToArray();
        Assert.Equal(2, collectionRequests.Length);
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_DuplicateIdentities_DoesNotThrowAndBothResolve()
    {
        var handler = RouteByMethodAndPath(
            (HttpMethod.Get, "/sets", SetsListJson),
            (
                HttpMethod.Post,
                "/cards/collection",
                """
                {
                  "object": "list",
                  "not_found": [],
                  "data": [
                    { "name": "My Precious", "set": "hob", "collector_number": "271", "image_uris": { "large": "https://cards.scryfall.io/large/my-precious.jpg" } }
                  ]
                }
                """
            )
        );
        var provider = NewProvider(handler);
        var identity = new CardIdentity("My Precious", "The Hobbit", "#271", null);
        CardIdentity[] identities = [identity, identity];

        var results = await provider.TryMatchImageUrlsAsync(identities, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("https://cards.scryfall.io/large/my-precious.jpg", results[identity]);
    }

    private static StubHttpMessageHandler RouteByMethodAndPath(
        params (HttpMethod Method, string PathSuffix, string Json)[] routes
    ) =>
        StubHttpMessageHandler.RespondingPerRequest(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            foreach (var (method, pathSuffix, json) in routes)
            {
                if (
                    request.Method != method
                    || !path.EndsWith(pathSuffix, StringComparison.Ordinal)
                )
                {
                    continue;
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json),
                };
            }

            throw new InvalidOperationException($"No stubbed route for '{request.Method} {path}'.");
        });

    private static ScryfallCardCatalogProvider NewProvider(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.scryfall.com/"),
        };
        var setCatalog = new ScryfallSetCatalog(
            new SingleClientHttpClientFactory(httpClient),
            new FakeTimeProvider(DateTimeOffset.UtcNow)
        );
        return new ScryfallCardCatalogProvider(httpClient, setCatalog);
    }
}
