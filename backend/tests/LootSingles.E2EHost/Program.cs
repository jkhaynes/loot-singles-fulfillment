using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using LootSingles.Api.Controllers;
using LootSingles.Application.Auth;
using LootSingles.Application.CardCatalog;
using LootSingles.Application.Dashboard;
using LootSingles.Application.Import;
using LootSingles.Application.Orders;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Import;
using LootSingles.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

const string FakePikachuImageUrl = "https://static.e2e-fixtures.local/pikachu.png";
const string FakeLightningBoltImageUrl = "https://static.e2e-fixtures.local/lightning-bolt.png";
const string FakeElsaImageUrl = "https://static.e2e-fixtures.local/elsa.png";
const string FailingProviderCollectorNumber = "#FAIL";
const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04";
await using var container = new MsSqlBuilder(SqlServerImage).Build();
await container.StartAsync();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5098");

builder.Services.AddDbContext<LootSinglesDbContext>(options =>
    options.UseSqlServer(container.GetConnectionString())
);

builder
    .Services.AddControllers()
    .AddApplicationPart(typeof(AuthController).Assembly)
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        );
    });
builder.Services.AddScoped<IPinHasher, Pbkdf2PinHasher>();
builder.Services.AddScoped<IEmployeeRepository, EmployeeRepository>();
builder.Services.AddSingleton(new LockoutOptions());
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<EmployeeManagementService>();
builder.Services.AddScoped<EmployeeSessionCookieEvents>();
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<IImportPersistence, ImportRepository>();
builder.Services.AddScoped<IPackingSlipParser, PdfPigPackingSlipParser>();
builder.Services.AddScoped<PackingSlipImportService>();
builder.Services.AddScoped<IPackingSlipImportService, ObservableProgressImportService>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<ICardCatalogProvider>(_ => new FakeCardCatalogProvider(
    "Pokemon",
    FakePikachuImageUrl,
    FailingProviderCollectorNumber
));
builder.Services.AddScoped<ICardCatalogProvider>(_ => new FakeCardCatalogProvider(
    "Magic",
    FakeLightningBoltImageUrl,
    FailingProviderCollectorNumber
));
builder.Services.AddScoped<ICardCatalogProvider>(_ => new FakeCardCatalogProvider(
    "Lorcana TCG",
    FakeElsaImageUrl,
    FailingProviderCollectorNumber
));
builder.Services.AddScoped<CardImageEnrichmentService>();
builder.Services.AddScoped<OrdersService>();
builder.Services.AddScoped<OrderClaimService>();
builder
    .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
        options.EventsType = typeof(EmployeeSessionCookieEvents);
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet(
    "/health",
    async (LootSinglesDbContext context) =>
        Results.Ok(
            new
            {
                DatabaseProvider = context.Database.ProviderName,
                Seeded = await context.Employees.AnyAsync(employee =>
                    employee.NormalizedUsername == "E2EMANAGER"
                )
                    && await context.Orders.AnyAsync(order =>
                        order.TcgplayerOrderId == "E2E-ORDER-00001"
                    ),
            }
        )
);

await SeedAsync(app.Services);
await app.RunAsync();

static async Task SeedAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
    var pinHasher = scope.ServiceProvider.GetRequiredService<IPinHasher>();
    await context.Database.MigrateAsync();
    context.Employees.Add(
        new Employee
        {
            Username = "e2emanager",
            NormalizedUsername = "E2EMANAGER",
            DisplayName = "E2E Manager",
            PinHash = pinHasher.Hash("1234"),
            Role = EmployeeRole.ManagerAdmin,
            CreatedAt = DateTimeOffset.UtcNow,
        }
    );
    context.Orders.Add(
        new Order
        {
            TcgplayerOrderId = "E2E-ORDER-00001",
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow,
            OrderLines =
            [
                new OrderLine
                {
                    RawDescription = "Pikachu - Base Set - #58/102 - Common - Near Mint",
                    ProductLine = "Pokemon",
                    ProductName = "Pikachu",
                    Set = "Base Set",
                    CollectorNumber = "#58/102",
                    Condition = "Near Mint",
                    Quantity = 2,
                },
                new OrderLine
                {
                    RawDescription =
                        "Simulated Provider Failure - Base Set - #FAIL - Common - Near Mint",
                    ProductLine = "Pokemon",
                    ProductName = "Simulated Provider Failure",
                    Set = "Base Set",
                    CollectorNumber = FailingProviderCollectorNumber,
                    Condition = "Near Mint",
                    Quantity = 1,
                },
                new OrderLine
                {
                    RawDescription = "Lightning Bolt - Alpha - #161 - Common - Near Mint",
                    ProductLine = "Magic",
                    ProductName = "Lightning Bolt",
                    Set = "Alpha",
                    CollectorNumber = "#161",
                    Condition = "Near Mint",
                    Quantity = 1,
                },
                new OrderLine
                {
                    RawDescription = "Elsa - The First Chapter - #207 - Legendary - Near Mint",
                    ProductLine = "Lorcana TCG",
                    ProductName = "Elsa",
                    Set = "The First Chapter",
                    CollectorNumber = "#207",
                    Condition = "Near Mint",
                    Quantity = 1,
                },
            ],
        }
    );
    await context.SaveChangesAsync();
}

internal sealed class FakeCardCatalogProvider(
    string productLine,
    string imageUrl,
    string failingCollectorNumber
) : ICardCatalogProvider
{
    public string ProductLine { get; } = productLine;

    public Task<string?> TryMatchImageUrlAsync(
        CardIdentity identity,
        CancellationToken cancellationToken
    ) =>
        identity.CollectorNumber == failingCollectorNumber
            ? throw new InvalidOperationException("Simulated provider failure.")
            : Task.FromResult<string?>(imageUrl);
}

internal sealed class ObservableProgressImportService(PackingSlipImportService inner)
    : IPackingSlipImportService
{
    public async IAsyncEnumerable<ImportProgressUpdate> ImportAsync(
        Stream packingSlipPdf,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (
            var update in inner
                .ImportAsync(packingSlipPdf, cancellationToken)
                .WithCancellation(cancellationToken)
        )
        {
            yield return update;

            if (
                !update.IsComplete
                && update.OrdersProcessed > 0
                && update.OrdersProcessed < update.OrdersDetected
            )
            {
                await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
            }
        }
    }
}
