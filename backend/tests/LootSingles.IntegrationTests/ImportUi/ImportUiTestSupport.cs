using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LootSingles.Api.Controllers;
using LootSingles.Application.Import;
using LootSingles.Domain.Employees;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Persistence;
using LootSingles.Infrastructure.Tcgplayer;
using LootSingles.IntegrationTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LootSingles.IntegrationTests.ImportUi;

internal static class ImportUiTestSupport
{
    public static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory)
    {
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
            context.Employees.Add(
                new Employee
                {
                    Username = "importer",
                    NormalizedUsername = "IMPORTER",
                    DisplayName = "Import User",
                    PinHash = new Pbkdf2PinHasher().Hash("1234"),
                    Role = EmployeeRole.Picker,
                    CreatedAt = DateTimeOffset.UtcNow,
                }
            );

            await context.SaveChangesAsync();
        }

        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }
        );

        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new LoginRequest("importer", "1234")
                )
            ).StatusCode
        );
        return client;
    }

    public static MultipartFormDataContent FileForm(
        byte[] bytes,
        string contentType = "application/pdf",
        string name = "file"
    )
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, name, "orders.pdf");
        return form;
    }

    public static async Task<string[]> PostFixtureAsync(HttpClient client, string fixture)
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "PackingSlips",
            fixture
        );
        var bytes = await File.ReadAllBytesAsync(fixturePath);
        using var form = FileForm(bytes);

        var response = await client.PostAsync("/api/imports", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        return content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Hosts the API with obviously fake TCGplayer keys (or none) and <paramref name="stub"/> as the
    /// PRIMARY handler of the "Tcgplayer" client only, so the real auth, rate-limit, timeout and
    /// User-Agent pipeline runs in front of it and no request can leave the process.
    /// </summary>
    public static WebApplicationFactory<Program> WithTcgplayerStub(
        WebApplicationFactory<Program> factory,
        TcgplayerStubHandler stub,
        bool configured = true
    ) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Tcgplayer:PublicKey", configured ? "synthetic-public-id" : "");
            builder.UseSetting("Tcgplayer:PrivateKey", configured ? "synthetic-private-id" : "");
            builder.UseSetting("Tcgplayer:AccessToken", configured ? "synthetic-store-access" : "");
            builder.UseSetting("Tcgplayer:StoreKey", "");
            builder.UseSetting("Tcgplayer:BaseUrl", TcgplayerStubHandler.BaseUrl);
            builder.UseSetting("Tcgplayer:PageSize", "2");
            builder.ConfigureServices(services =>
                services
                    .AddHttpClient(TcgplayerServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => stub)
            );
        });

    /// <summary>
    /// Reads a TCGplayer NDJSON response to the end and returns its lines.
    /// </summary>
    public static async Task<string[]> PostTcgplayerAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/imports/tcgplayer", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        return content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}
