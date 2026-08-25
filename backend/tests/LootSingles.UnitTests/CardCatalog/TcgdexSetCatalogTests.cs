using System.Net;
using LootSingles.Infrastructure.CardCatalog;

namespace LootSingles.UnitTests.CardCatalog;

public sealed class TcgdexSetCatalogTests
{
    private const string SetsListJson = """
        [
          { "id": "sv10.5b", "name": "Black Bolt" },
          { "id": "sv10.5w", "name": "White Flare" }
        ]
        """;

    [Fact]
    public async Task GetSetIdsByNameAsync_CalledTwiceWithinCacheDuration_FetchesTheSetsListOnlyOnce()
    {
        var handler = StubHttpMessageHandler.ReturningJson(SetsListJson);
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var catalog = NewCatalog(handler, timeProvider);

        await catalog.GetSetIdsByNameAsync(CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromHours(1));
        await catalog.GetSetIdsByNameAsync(CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetSetIdsByNameAsync_CalledAfterCacheDurationElapses_RefetchesTheSetsList()
    {
        var handler = StubHttpMessageHandler.ReturningJson(SetsListJson);
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var catalog = NewCatalog(handler, timeProvider);

        await catalog.GetSetIdsByNameAsync(CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromHours(25));
        await catalog.GetSetIdsByNameAsync(CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetSetIdsByNameAsync_FetchFails_RetriesOnTheNextCallRatherThanCachingTheFailure()
    {
        var attempt = 0;
        var handler = StubHttpMessageHandler.RespondingPerRequest(_ =>
        {
            attempt++;
            return attempt == 1
                ? throw new HttpRequestException("Simulated transient TCGdex failure.")
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SetsListJson),
                };
        });
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var catalog = NewCatalog(handler, timeProvider);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            catalog.GetSetIdsByNameAsync(CancellationToken.None)
        );
        // Still well within the 24-hour cache duration - a healthy cache would retry anyway.
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var result = await catalog.GetSetIdsByNameAsync(CancellationToken.None);

        Assert.Equal("sv10.5b", result["Black Bolt"]);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetSetIdsByNameAsync_ReturnsTheDictionaryKeyedBySetName()
    {
        var handler = StubHttpMessageHandler.ReturningJson(SetsListJson);
        var catalog = NewCatalog(handler, new FakeTimeProvider(DateTimeOffset.UtcNow));

        var result = await catalog.GetSetIdsByNameAsync(CancellationToken.None);

        Assert.Equal("sv10.5b", result["Black Bolt"]);
        Assert.Equal("sv10.5w", result["White Flare"]);
    }

    private static TcgdexSetCatalog NewCatalog(
        HttpMessageHandler handler,
        FakeTimeProvider timeProvider
    )
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.tcgdex.net/v2/en/"),
        };
        return new TcgdexSetCatalog(new SingleClientHttpClientFactory(httpClient), timeProvider);
    }
}
