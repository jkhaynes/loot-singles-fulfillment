using System.Net;
using LootSingles.Infrastructure.CardCatalog;

namespace LootSingles.UnitTests.CardCatalog;

public sealed class LorcastSetCatalogTests
{
    private const string SetsListJson = """
        {
          "results": [
            { "id": "set_7ecb0e0c71af496a9e0110e23824e0a5", "code": "1", "name": "The First Chapter" },
            { "id": "set_d100", "code": "D100", "name": "Disney100" }
          ]
        }
        """;

    [Fact]
    public async Task GetSetCodesByNameAsync_CalledTwiceWithinCacheDuration_FetchesTheSetsListOnlyOnce()
    {
        var handler = StubHttpMessageHandler.ReturningJson(SetsListJson);
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var catalog = NewCatalog(handler, timeProvider);

        await catalog.GetSetCodesByNameAsync(CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromHours(1));
        await catalog.GetSetCodesByNameAsync(CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetSetCodesByNameAsync_CalledAfterCacheDurationElapses_RefetchesTheSetsList()
    {
        var handler = StubHttpMessageHandler.ReturningJson(SetsListJson);
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var catalog = NewCatalog(handler, timeProvider);

        await catalog.GetSetCodesByNameAsync(CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromHours(25));
        await catalog.GetSetCodesByNameAsync(CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetSetCodesByNameAsync_FetchFails_RetriesOnTheNextCallRatherThanCachingTheFailure()
    {
        var attempt = 0;
        var handler = StubHttpMessageHandler.RespondingPerRequest(_ =>
        {
            attempt++;
            return attempt == 1
                ? throw new HttpRequestException("Simulated transient Lorcast failure.")
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SetsListJson),
                };
        });
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var catalog = NewCatalog(handler, timeProvider);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            catalog.GetSetCodesByNameAsync(CancellationToken.None)
        );
        // Still well within the 24-hour cache duration - a healthy cache would retry anyway.
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var result = await catalog.GetSetCodesByNameAsync(CancellationToken.None);

        Assert.Equal(["1"], result["The First Chapter"]);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetSetCodesByNameAsync_ReturnsTheDictionaryKeyedBySetName()
    {
        var handler = StubHttpMessageHandler.ReturningJson(SetsListJson);
        var catalog = NewCatalog(handler, new FakeTimeProvider(DateTimeOffset.UtcNow));

        var result = await catalog.GetSetCodesByNameAsync(CancellationToken.None);

        Assert.Equal(["1"], result["The First Chapter"]);
        Assert.Equal(["D100"], result["Disney100"]);
    }

    [Fact]
    public async Task GetSetCodesByNameAsync_PromoShapedSetNames_AreGroupedUnderTheGenericTcgplayerLabel()
    {
        // TCGplayer collapses every Lorcana promo drop under one generic label ("Disney Lorcana
        // Promo Cards") that matches no single Lorcast set name - Lorcast instead publishes each
        // drop as its own separate, numbered set. Confirmed live against Lorcast's real /v0/sets
        // data (research.md §5 update).
        const string promoSetsJson = """
            {
              "results": [
                { "id": "set_p1", "code": "P1", "name": "Promo Set 1" },
                { "id": "set_p2", "code": "P2", "name": "Promo Set 2" },
                { "id": "set_cp", "code": "cp", "name": "Challenge Promo" },
                { "id": "set_1", "code": "1", "name": "The First Chapter" }
              ]
            }
            """;
        var handler = StubHttpMessageHandler.ReturningJson(promoSetsJson);
        var catalog = NewCatalog(handler, new FakeTimeProvider(DateTimeOffset.UtcNow));

        var result = await catalog.GetSetCodesByNameAsync(CancellationToken.None);

        Assert.Equal(["P1", "P2", "cp"], result["Disney Lorcana Promo Cards"]);
    }

    private static LorcastSetCatalog NewCatalog(
        HttpMessageHandler handler,
        FakeTimeProvider timeProvider
    )
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.lorcast.com/v0/"),
        };
        return new LorcastSetCatalog(new SingleClientHttpClientFactory(httpClient), timeProvider);
    }
}
