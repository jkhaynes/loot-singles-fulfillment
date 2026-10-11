using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LootSingles.Api.Controllers;
using LootSingles.Application.CardCatalog;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LootSingles.IntegrationTests.Orders;

public sealed class TcgplayerOrderImageTests
{
    [Fact]
    public async Task GetByIdForTcgplayerApiOrder_ReturnsStoredImageUrlsAndNeverCallsTheCatalogProvider()
    {
        var provider = new RecordingCardCatalogProvider("Pokemon");
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = WithProvider(rootFactory, provider);

        var orderId = await SeedAsync(
            factory,
            "SYN-0001-IMG-API",
            OrderImportSource.TcgplayerApi,
            NewLine(
                "Synthetic Dragon",
                "#012/100",
                "https://example.invalid/stored-dragon.jpg",
                "English"
            ),
            NewLine("Synthetic Booster Box", null, null, "Japanese")
        );

        var lines = await GetLinesAsync(factory, orderId);

        Assert.Equal(
            "https://example.invalid/stored-dragon.jpg",
            lines[0].GetProperty("imageUrl").GetString()
        );
        Assert.Equal(JsonValueKind.Null, lines[1].GetProperty("imageUrl").ValueKind);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task GetByIdForTcgplayerApiOrder_ReturnsNullCollectorNumberAndLanguage()
    {
        var provider = new RecordingCardCatalogProvider("Pokemon");
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = WithProvider(rootFactory, provider);

        var orderId = await SeedAsync(
            factory,
            "SYN-0002-IMG-API",
            OrderImportSource.TcgplayerApi,
            NewLine("Synthetic Dragon", "#012/100", null, "English"),
            NewLine("Synthetic Booster Box", null, null, "Japanese")
        );

        var lines = await GetLinesAsync(factory, orderId);

        Assert.Equal("#012/100", lines[0].GetProperty("collectorNumber").GetString());
        Assert.Equal(JsonValueKind.Null, lines[1].GetProperty("collectorNumber").ValueKind);
        Assert.Equal("English", lines[0].GetProperty("language").GetString());
        Assert.Equal("Japanese", lines[1].GetProperty("language").GetString());
    }

    [Fact]
    public async Task GetByIdForPackingSlipPdfOrder_StillCallsTheProviderAndResolvesImages()
    {
        var provider = new RecordingCardCatalogProvider(
            "Pokemon",
            "https://example.invalid/resolved.png"
        );
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = WithProvider(rootFactory, provider);

        var orderId = await SeedAsync(
            factory,
            "SYN-0003-IMG-PDF",
            OrderImportSource.PackingSlipPdf,
            NewLine("Synthetic Dragon", "#012/100", null, null)
        );

        var lines = await GetLinesAsync(factory, orderId);

        Assert.Equal(
            "https://example.invalid/resolved.png",
            lines[0].GetProperty("imageUrl").GetString()
        );
        Assert.Equal(1, provider.CallCount);
    }

    private static WebApplicationFactory<Program> WithProvider(
        AuthWebApplicationFactory rootFactory,
        ICardCatalogProvider provider
    ) =>
        rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICardCatalogProvider>();
                services.AddScoped(_ => provider);
            })
        );

    private static async Task<int> SeedAsync(
        WebApplicationFactory<Program> factory,
        string tcgplayerOrderId,
        OrderImportSource source,
        params OrderLine[] lines
    )
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        context.Employees.Add(
            new Employee
            {
                Username = "tcgimageuser",
                NormalizedUsername = "TCGIMAGEUSER",
                DisplayName = "Tcg Image User",
                PinHash = new Pbkdf2PinHasher().Hash("1234"),
                Role = EmployeeRole.Picker,
                CreatedAt = DateTimeOffset.UtcNow,
            }
        );
        var order = new Order
        {
            TcgplayerOrderId = tcgplayerOrderId,
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.Parse("2026-10-10T12:00:00Z"),
            ImportSource = source,
        };
        foreach (var line in lines)
        {
            order.OrderLines.Add(line);
        }
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private static async Task<JsonElement[]> GetLinesAsync(
        WebApplicationFactory<Program> factory,
        int orderId
    )
    {
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }
        );
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("tcgimageuser", "1234")
        );
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var response = await client.GetAsync($"/api/orders/{orderId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document
            .RootElement.GetProperty("lines")
            .EnumerateArray()
            .Select(line => line.Clone())
            .ToArray();
    }

    private static OrderLine NewLine(
        string name,
        string? collectorNumber,
        string? imageUrl,
        string? language
    ) =>
        new()
        {
            RawDescription = name,
            ProductLine = "Pokemon",
            ProductName = name,
            Set = "Synthetic Set",
            CollectorNumber = collectorNumber,
            Condition = "Near Mint",
            Quantity = 1,
            ImageUrl = imageUrl,
            Language = language,
        };

    private sealed class RecordingCardCatalogProvider(string productLine, string? imageUrl = null)
        : ICardCatalogProvider
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public string ProductLine { get; } = productLine;

        public Task<string?> TryMatchImageUrlAsync(
            CardIdentity identity,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(imageUrl);
        }
    }
}
