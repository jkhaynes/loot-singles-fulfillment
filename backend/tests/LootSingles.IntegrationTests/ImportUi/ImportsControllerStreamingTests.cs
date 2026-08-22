using System.Text.Json;
using LootSingles.IntegrationTests.Auth;

namespace LootSingles.IntegrationTests.ImportUi;

public sealed class ImportsControllerStreamingTests
{
    [Fact]
    public async Task MixedBatchStreamsProcessedSnapshotsAndOneCompletedTerminal()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await ImportUiTestSupport.LoginAsync(factory);

        var lines = await ImportUiTestSupport.PostFixtureAsync(
            client,
            "partial-batch-one-bad-order.pdf"
        );
        var documents = lines.Select(line => JsonDocument.Parse(line)).ToArray();

        Assert.Single(
            documents,
            document => document.RootElement.GetProperty("status").GetString() == "completed"
        );
        Assert.All(
            documents[..^1],
            document =>
                Assert.Equal("inProgress", document.RootElement.GetProperty("status").GetString())
        );
        Assert.Equal(3, documents[^1].RootElement.GetProperty("ordersProcessed").GetInt32());
        Assert.Equal(1, documents[^1].RootElement.GetProperty("failedCount").GetInt32());
    }
}
