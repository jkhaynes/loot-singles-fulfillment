using LootSingles.Infrastructure.CardCatalog;

namespace LootSingles.UnitTests.CardCatalog;

public sealed class CandidateImageResolverTests
{
    [Fact]
    public void Resolve_ExactlyOneValidImage_ReturnsIt()
    {
        var loggedCounts = new List<int>();

        var result = CandidateImageResolver.Resolve(
            ["https://example.test/card.jpg"],
            count => loggedCounts.Add(count)
        );

        Assert.Equal("https://example.test/card.jpg", result);
        Assert.Empty(loggedCounts);
    }

    [Fact]
    public void Resolve_NoValidImages_ReturnsNullWithoutLoggingAmbiguous()
    {
        var loggedCounts = new List<int>();

        var result = CandidateImageResolver.Resolve([], count => loggedCounts.Add(count));

        Assert.Null(result);
        Assert.Empty(loggedCounts);
    }

    [Fact]
    public void Resolve_MultipleValidImages_LogsAmbiguousCountAndReturnsNull()
    {
        var loggedCounts = new List<int>();

        var result = CandidateImageResolver.Resolve(
            ["https://example.test/a.jpg", "https://example.test/b.jpg"],
            count => loggedCounts.Add(count)
        );

        Assert.Null(result);
        Assert.Equal([2], loggedCounts);
    }
}
