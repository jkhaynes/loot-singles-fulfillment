using System.Text.Json;
using LootSingles.IntegrationTests.Auth;

namespace LootSingles.IntegrationTests.ImportUi;

public sealed class ImportsControllerDuplicateAndWarningTests
{
    [Fact]
    public async Task DuplicateUnreadableAndMismatchRemainTyped()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await ImportUiTestSupport.LoginAsync(factory);

        await ImportUiTestSupport.PostFixtureAsync(client, "valid-multi-order-batch.pdf");
        var duplicateLines = await ImportUiTestSupport.PostFixtureAsync(
            client,
            "valid-multi-order-batch.pdf"
        );
        using var duplicate = JsonDocument.Parse(duplicateLines[^1]);

        Assert.Contains(
            duplicate.RootElement.GetProperty("results").EnumerateArray(),
            item => item.GetProperty("failureCode").GetString() == "duplicateOrder"
        );

        var unreadableLines = await ImportUiTestSupport.PostFixtureAsync(
            client,
            "corrupted-file.pdf"
        );
        using var unreadable = JsonDocument.Parse(unreadableLines[^1]);

        Assert.Equal(
            "unreadablePdf",
            unreadable.RootElement.GetProperty("attemptFailureCode").GetString()
        );

        var mismatchLines = await ImportUiTestSupport.PostFixtureAsync(
            client,
            "summary-mismatch.pdf"
        );
        using var mismatch = JsonDocument.Parse(mismatchLines[^1]);

        Assert.Equal(
            "summaryMismatch",
            mismatch.RootElement.GetProperty("attemptFailureCode").GetString()
        );
    }
}
