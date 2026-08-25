using LootSingles.Infrastructure.CardCatalog;

namespace LootSingles.UnitTests.CardCatalog;

public sealed class ScryfallSetCatalogTests
{
    private const string SetsListJson = """
        {
          "object": "list",
          "data": [
            { "code": "aer", "name": "Aether Revolt" },
            { "code": "xln", "name": "Ixalan" }
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
                ? throw new HttpRequestException("Simulated transient Scryfall failure.")
                : new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(SetsListJson),
                };
        });
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var catalog = NewCatalog(handler, timeProvider);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            catalog.GetSetCodesByNameAsync(CancellationToken.None)
        );
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var result = await catalog.GetSetCodesByNameAsync(CancellationToken.None);

        Assert.Equal("aer", result["Aether Revolt"]);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetSetCodesByNameAsync_ReturnsTheDictionaryKeyedBySetName()
    {
        var handler = StubHttpMessageHandler.ReturningJson(SetsListJson);
        var catalog = NewCatalog(handler, new FakeTimeProvider(DateTimeOffset.UtcNow));

        var result = await catalog.GetSetCodesByNameAsync(CancellationToken.None);

        Assert.Equal("aer", result["Aether Revolt"]);
        Assert.Equal("xln", result["Ixalan"]);
    }

    private static ScryfallSetCatalog NewCatalog(
        HttpMessageHandler handler,
        FakeTimeProvider timeProvider
    )
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.scryfall.com/"),
        };
        return new ScryfallSetCatalog(new SingleClientHttpClientFactory(httpClient), timeProvider);
    }
}
