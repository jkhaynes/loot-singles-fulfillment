using System.Net;
using LootSingles.Api.Controllers;
using LootSingles.IntegrationTests.Auth;

namespace LootSingles.IntegrationTests.ImportUi;

public sealed class ImportsControllerValidationTests
{
    [Fact]
    public async Task MissingFileReturns400AndNonPdfReturns415()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        using var emptyForm = new MultipartFormDataContent();

        var missingFileResponse = await client.PostAsync("/api/imports", emptyForm);

        using var textFileForm = ImportUiTestSupport.FileForm([1], "text/plain");
        var nonPdfResponse = await client.PostAsync("/api/imports", textFileForm);

        Assert.Equal(HttpStatusCode.BadRequest, missingFileResponse.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, nonPdfResponse.StatusCode);
    }

    [Fact]
    public async Task FileOver25MbReturns413()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        using var form = ImportUiTestSupport.FileForm(
            new byte[ImportsController.MaximumFileBytes + 1]
        );

        var response = await client.PostAsync("/api/imports", form);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }
}
