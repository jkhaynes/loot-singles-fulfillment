using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using LootSingles.Api.Controllers;
using LootSingles.Application.Import;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LootSingles.IntegrationTests.ImportUi;

public sealed class ImportsControllerValidationTests
{
    [Fact]
    public async Task MissingMultipleWrongNamedAndMalformedFilesReturn400WithoutProcessing()
    {
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(ConfigureSpy)
        );
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        var spy = factory.Services.GetRequiredService<CountingImportService>();

        using var emptyForm = new MultipartFormDataContent();
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsync("/api/imports", emptyForm)).StatusCode
        );

        using var multipleForm = ImportUiTestSupport.FileForm([1]);
        var secondFile = new ByteArrayContent([2]);
        secondFile.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        multipleForm.Add(secondFile, "file", "second.pdf");
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsync("/api/imports", multipleForm)).StatusCode
        );

        using var wrongNameForm = ImportUiTestSupport.FileForm([1], name: "document");
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsync("/api/imports", wrongNameForm)).StatusCode
        );

        using var malformedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/imports");
        malformedRequest.Content = new ByteArrayContent("not multipart framing"u8.ToArray());
        malformedRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(
            "multipart/form-data; boundary=broken"
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.SendAsync(malformedRequest)).StatusCode
        );

        Assert.Equal(0, spy.InvocationCount);
        await AssertNoOrdersAsync(factory.Services);
    }

    [Fact]
    public async Task NonPdfAndFileOver25MiBAreRejectedWithoutProcessing()
    {
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(ConfigureSpy)
        );
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        var spy = factory.Services.GetRequiredService<CountingImportService>();

        using var textFileForm = ImportUiTestSupport.FileForm([1], "text/plain");
        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            (await client.PostAsync("/api/imports", textFileForm)).StatusCode
        );

        using var oversizedForm = ImportUiTestSupport.FileForm(
            new byte[ImportsController.MaximumFileBytes + 1]
        );
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            (await client.PostAsync("/api/imports", oversizedForm)).StatusCode
        );

        Assert.Equal(0, spy.InvocationCount);
        await AssertNoOrdersAsync(factory.Services);
    }

    [Fact]
    public async Task FileExactlyAt25MiBBoundaryReachesImportProcessing()
    {
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(ConfigureSpy)
        );
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        var spy = factory.Services.GetRequiredService<CountingImportService>();
        using var boundaryForm = ImportUiTestSupport.FileForm(
            new byte[ImportsController.MaximumFileBytes]
        );

        var response = await client.PostAsync("/api/imports", boundaryForm);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, spy.InvocationCount);
    }

    private static void ConfigureSpy(IServiceCollection services)
    {
        services.RemoveAll<IPackingSlipImportService>();
        services.AddSingleton<CountingImportService>();
        services.AddSingleton<IPackingSlipImportService>(provider =>
            provider.GetRequiredService<CountingImportService>()
        );
    }

    private static async Task AssertNoOrdersAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        Assert.False(await context.Orders.AnyAsync());
    }

    private sealed class CountingImportService : IPackingSlipImportService
    {
        private int _invocationCount;

        public int InvocationCount => _invocationCount;

        public async IAsyncEnumerable<ImportProgressUpdate> ImportAsync(
            Stream packingSlipPdf,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref _invocationCount);
            await Task.Yield();
            yield return new ImportProgressUpdate(
                0,
                0,
                0,
                0,
                true,
                new ImportAttempt { StartedAt = DateTimeOffset.UtcNow }
            );
        }
    }
}
