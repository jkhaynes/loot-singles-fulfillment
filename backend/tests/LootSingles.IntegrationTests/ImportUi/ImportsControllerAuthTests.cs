using System.Net;
using LootSingles.IntegrationTests.Auth;

namespace LootSingles.IntegrationTests.ImportUi;

public sealed class ImportsControllerAuthTests
{
    [Fact]
    public async Task PostWithoutSessionReturns401()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.EnsureDatabaseCreatedAsync();
        using var client = factory.CreateAuthenticatedClient();
        using var form = ImportUiTestSupport.FileForm([1]);

        var response = await client.PostAsync("/api/imports", form);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
