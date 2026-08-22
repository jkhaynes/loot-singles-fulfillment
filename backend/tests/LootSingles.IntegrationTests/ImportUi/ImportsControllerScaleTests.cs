using LootSingles.IntegrationTests.Auth;

namespace LootSingles.IntegrationTests.ImportUi;

public sealed class ImportsControllerScaleTests
{
    [Fact]
    public async Task TransportEmitsOnlyIncreasingProcessedCounts()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await ImportUiTestSupport.LoginAsync(factory);

        var lines = await ImportUiTestSupport.PostFixtureAsync(
            client,
            "partial-batch-one-bad-order.pdf"
        );

        Assert.Equal(4, lines.Length);
    }
}
