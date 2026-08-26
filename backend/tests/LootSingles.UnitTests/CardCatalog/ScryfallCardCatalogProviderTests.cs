using System.Net;
using System.Text;
using System.Text.Json;
using LootSingles.Application.CardCatalog;
using LootSingles.Infrastructure.CardCatalog;
using Microsoft.Extensions.Logging.Abstractions;

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
    public async Task TryMatchImageUrlsAsync_AyaraCollector13_ResolvesThroughMul()
    {
        var handler = RouteByMethodAndPath(
            (
                HttpMethod.Post,
                "/cards/collection",
                """
                {
                  "data": [
                    { "name": "Ayara, First of Locthwain", "set": "mul", "collector_number": "13", "image_uris": { "large": "https://cards.scryfall.io/large/ayara.jpg" } }
                  ]
                }
                """
            )
        );
        var provider = NewProvider(
            handler,
            Mapping("March of the Machine: Multiverse Legends", "mul")
        );
        var identity = new CardIdentity(
            "Ayara, First of Locthwain",
            "March of the Machine: Multiverse Legends",
            "#13",
            "Foil"
        );

        var results = await provider.TryMatchImageUrlsAsync([identity], CancellationToken.None);

        Assert.Equal("https://cards.scryfall.io/large/ayara.jpg", results[identity]);
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_EmptyCandidateArray_ReturnsNullWithoutRequest()
    {
        var handler = RouteByMethodAndPath();
        var provider = NewProvider(handler, Mapping("Known Empty Set"));
        var identity = new CardIdentity("Card", "Known Empty Set", "#1", null);

        var results = await provider.TryMatchImageUrlsAsync([identity], CancellationToken.None);

        Assert.Null(results[identity]);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_MultipleCandidates_EvaluatesEveryCandidateAndReturnsExactlyOneValid()
    {
        var handler = RouteByMethodAndPath(
            (
                HttpMethod.Post,
                "/cards/collection",
                """
                {
                  "data": [
                    { "name": "Wrong Card", "set": "aaa", "collector_number": "7", "image_uris": { "large": "https://cards.scryfall.io/large/wrong.jpg" } },
                    { "name": "Right Card", "set": "bbb", "collector_number": "7", "image_uris": { "large": "https://cards.scryfall.io/large/right.jpg" } }
                  ]
                }
                """
            )
        );
        var provider = NewProvider(handler, Mapping("Shared TCGplayer Set", "aaa", "bbb"));
        var identity = new CardIdentity("Right Card", "Shared TCGplayer Set", "#7", null);

        var results = await provider.TryMatchImageUrlsAsync([identity], CancellationToken.None);

        Assert.Equal("https://cards.scryfall.io/large/right.jpg", results[identity]);
        var requestBody = await handler.Requests.Single().Content!.ReadAsStringAsync();
        Assert.Contains("\"set\":\"aaa\"", requestBody);
        Assert.Contains("\"set\":\"bbb\"", requestBody);
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_MultipleValidCandidates_ReturnsNull()
    {
        var handler = RouteByMethodAndPath(
            (
                HttpMethod.Post,
                "/cards/collection",
                """
                {
                  "data": [
                    { "name": "Same Card", "set": "aaa", "collector_number": "7", "image_uris": { "large": "https://cards.scryfall.io/large/a.jpg" } },
                    { "name": "Same Card", "set": "bbb", "collector_number": "7", "image_uris": { "large": "https://cards.scryfall.io/large/b.jpg" } }
                  ]
                }
                """
            )
        );
        var provider = NewProvider(handler, Mapping("Shared TCGplayer Set", "aaa", "bbb"));
        var identity = new CardIdentity("Same Card", "Shared TCGplayer Set", "#7", null);

        var results = await provider.TryMatchImageUrlsAsync([identity], CancellationToken.None);

        Assert.Null(results[identity]);
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
    public async Task TryMatchImageUrlsAsync_SetNameHasMidWordHyphenLineWrap_StillResolves()
    {
        // Confirmed real-order case: PDF text extraction turned the line-wrapped "Middle-earth"
        // into "Middle- earth" (a stray space after the hyphen, none before it), which does not
        // match any crosswalk entry as-is.
        var handler = RouteByMethodAndPath(
            (
                HttpMethod.Post,
                "/cards/collection",
                """
                {
                  "object": "list",
                  "not_found": [],
                  "data": [
                    { "name": "Samwise", "set": "ltr", "collector_number": "39", "image_uris": { "large": "https://cards.scryfall.io/large/samwise.jpg" } }
                  ]
                }
                """
            )
        );
        var provider = NewProvider(
            handler,
            Mapping("Universes Beyond: The Lord of the Rings: Tales of Middle-earth", "ltr")
        );
        var identity = new CardIdentity(
            "Samwise",
            "Universes Beyond: The Lord of the Rings: Tales of Middle- earth",
            "#39",
            null
        );

        var results = await provider.TryMatchImageUrlsAsync([identity], CancellationToken.None);

        Assert.Equal("https://cards.scryfall.io/large/samwise.jpg", results[identity]);
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
        // Every identity must actually resolve in the first phase, or the letter-suffix retry
        // phase would fire and add its own chunked requests on top - conflating this test's
        // concern (base-phase chunking) with that separately-tested behavior. Each chunk's
        // response must therefore only echo back cards for the collector numbers *that chunk*
        // requested, matching how the real API behaves (a fixed response echoed to every chunk
        // would duplicate every card across chunks and fail the "exactly one candidate" check).
        var handler = RespondingWithMatchingCardsPerChunk("hob", "Card");
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

    [Fact]
    public async Task TryMatchImageUrlsAsync_BaseCollectorNumberNotFound_RetriesWithAttractionLetterSuffixes()
    {
        // Confirmed real-order case: Unfinity "Attraction" cards can print a letter-suffixed
        // collector number on Scryfall (e.g. "222a") that TCGplayer's packing slip omits ("#222").
        var handler = RouteByMethodAndPath(
            (
                HttpMethod.Post,
                "/cards/collection",
                """
                {
                  "object": "list",
                  "not_found": [{ "set": "unf", "collector_number": "222" }],
                  "data": [
                    { "name": "Ferris Wheel", "set": "unf", "collector_number": "222a", "image_uris": { "large": "https://cards.scryfall.io/large/ferris-wheel.jpg" } }
                  ]
                }
                """
            )
        );
        var provider = NewProvider(handler, Mapping("Unfinity", "unf"));
        var identity = new CardIdentity("Ferris Wheel", "Unfinity", "#222", null);

        var results = await provider.TryMatchImageUrlsAsync([identity], CancellationToken.None);

        Assert.Equal("https://cards.scryfall.io/large/ferris-wheel.jpg", results[identity]);
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_MultipleAttractionLetterVariantsMatch_ReturnsNull()
    {
        // Confirmed real case: some Attraction base numbers have multiple distinct real prints
        // sharing it (e.g. Scavenger Hunt: 226a/226b/226c) - never guess between them.
        var handler = RouteByMethodAndPath(
            (
                HttpMethod.Post,
                "/cards/collection",
                """
                {
                  "object": "list",
                  "not_found": [{ "set": "unf", "collector_number": "226" }],
                  "data": [
                    { "name": "Scavenger Hunt", "set": "unf", "collector_number": "226a", "image_uris": { "large": "https://cards.scryfall.io/large/a.jpg" } },
                    { "name": "Scavenger Hunt", "set": "unf", "collector_number": "226b", "image_uris": { "large": "https://cards.scryfall.io/large/b.jpg" } },
                    { "name": "Scavenger Hunt", "set": "unf", "collector_number": "226c", "image_uris": { "large": "https://cards.scryfall.io/large/c.jpg" } }
                  ]
                }
                """
            )
        );
        var provider = NewProvider(handler, Mapping("Unfinity", "unf"));
        var identity = new CardIdentity("Scavenger Hunt", "Unfinity", "#226", null);

        var results = await provider.TryMatchImageUrlsAsync([identity], CancellationToken.None);

        Assert.Null(results[identity]);
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_BaseCollectorNumberResolves_DoesNotAttemptLetterSuffixRetry()
    {
        var handler = RouteByMethodAndPath(
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
        var provider = NewProvider(handler, Mapping("The Hobbit", "hob"));
        var identity = new CardIdentity("My Precious", "The Hobbit", "#271", null);

        var results = await provider.TryMatchImageUrlsAsync([identity], CancellationToken.None);

        Assert.Equal("https://cards.scryfall.io/large/my-precious.jpg", results[identity]);
        Assert.Single(
            handler.Requests,
            r => r.RequestUri!.AbsolutePath.EndsWith("/cards/collection")
        );
    }

    private static StubHttpMessageHandler RespondingWithMatchingCardsPerChunk(
        string setCode,
        string namePrefix
    ) =>
        StubHttpMessageHandler.RespondingPerRequest(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/sets", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SetsListJson),
                };
            }

            var body = request.Content!.ReadAsStringAsync().Result;
            using var document = JsonDocument.Parse(body);
            var collectorNumbers = document
                .RootElement.GetProperty("identifiers")
                .EnumerateArray()
                .Select(identifier => identifier.GetProperty("collector_number").GetString())
                .ToArray();
            var cardsJson = string.Join(
                ",",
                collectorNumbers.Select(number =>
                    $$"""{ "name": "{{namePrefix}} {{number}}", "set": "{{setCode}}", "collector_number": "{{number}}", "image_uris": { "large": "https://cards.scryfall.io/large/card-{{number}}.jpg" } }"""
                )
            );
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{ "object": "list", "not_found": [], "data": [{{cardsJson}}] }"""
                ),
            };
        });

    [Fact]
    public async Task TryMatchImageUrlsAsync_VariantClaimsFoilButCardIsNonfoilOnly_ReturnsNull()
    {
        var handler = RouteByMethodAndPath(
            (
                HttpMethod.Post,
                "/cards/collection",
                """
                {
                  "object": "list",
                  "not_found": [],
                  "data": [
                    { "name": "My Precious", "set": "hob", "collector_number": "271", "finishes": ["nonfoil"], "image_uris": { "large": "https://cards.scryfall.io/large/my-precious.jpg" } }
                  ]
                }
                """
            )
        );
        var provider = NewProvider(handler);
        CardIdentity[] identities = [new("My Precious", "The Hobbit", "#271", "Foil")];

        var results = await provider.TryMatchImageUrlsAsync(identities, CancellationToken.None);

        Assert.Null(results[identities[0]]);
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_VariantClaimsFoilAndCardHasFoilFinish_ReturnsImageUrl()
    {
        var handler = RouteByMethodAndPath(
            (
                HttpMethod.Post,
                "/cards/collection",
                """
                {
                  "object": "list",
                  "not_found": [],
                  "data": [
                    { "name": "My Precious", "set": "hob", "collector_number": "271", "finishes": ["nonfoil", "foil"], "image_uris": { "large": "https://cards.scryfall.io/large/my-precious.jpg" } }
                  ]
                }
                """
            )
        );
        var provider = NewProvider(handler);
        CardIdentity[] identities = [new("My Precious", "The Hobbit", "#271", "Foil")];

        var results = await provider.TryMatchImageUrlsAsync(identities, CancellationToken.None);

        Assert.Equal("https://cards.scryfall.io/large/my-precious.jpg", results[identities[0]]);
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_NoVariantClaim_IgnoresFinishesEvenWhenNonfoilOnly()
    {
        // Asymmetric by design (research.md): only reject when the packing slip's Variant text
        // explicitly claims "Foil" and Scryfall says the print has no foil-type finish at all.
        // A silent/absent Variant never triggers a rejection, even against a nonfoil-exclusive
        // print, since we have no confirmed evidence TCGplayer always labels foil copies
        // consistently - a false rejection here would regress FR-001 for no confirmed benefit.
        var handler = RouteByMethodAndPath(
            (
                HttpMethod.Post,
                "/cards/collection",
                """
                {
                  "object": "list",
                  "not_found": [],
                  "data": [
                    { "name": "My Precious", "set": "hob", "collector_number": "271", "finishes": ["nonfoil"], "image_uris": { "large": "https://cards.scryfall.io/large/my-precious.jpg" } }
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

    private static ScryfallCardCatalogProvider NewProvider(
        StubHttpMessageHandler handler,
        string? crosswalkJson = null
    )
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.scryfall.com/"),
        };
        using var stream = new MemoryStream(
            Encoding.UTF8.GetBytes(
                crosswalkJson
                    ?? """
                    {
                      "mappings": {
                        "The Hobbit": { "normalizedTcgplayerSetName": "the hobbit", "scryfallSetCodes": ["hob"] },
                        "Commander: FINAL FANTASY": { "normalizedTcgplayerSetName": "commander: final fantasy", "scryfallSetCodes": ["fic"] }
                      }
                    }
                    """
            )
        );
        var crosswalk = MagicSetCrosswalk.Load(stream);
        return new ScryfallCardCatalogProvider(
            httpClient,
            crosswalk,
            NullLogger<ScryfallCardCatalogProvider>.Instance
        );
    }

    private static string Mapping(string setName, params string[] codes)
    {
        var serializedCodes = string.Join(",", codes.Select(code => $"\"{code}\""));
        return $$"""
            {
              "mappings": {
                "{{setName}}": {
                  "normalizedTcgplayerSetName": "{{setName.ToLowerInvariant()}}",
                  "scryfallSetCodes": [{{serializedCodes}}]
                }
              }
            }
            """;
    }
}
