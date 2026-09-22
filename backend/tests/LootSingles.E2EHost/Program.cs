using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using LootSingles.Api.Controllers;
using LootSingles.Application.Auth;
using LootSingles.Application.CardCatalog;
using LootSingles.Application.Dashboard;
using LootSingles.Application.Import;
using LootSingles.Application.Orders;
using LootSingles.Application.Packing;
using LootSingles.Application.Picking;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Import;
using LootSingles.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

const string FakePikachuImageUrl = "https://static.e2e-fixtures.local/pikachu.png";
const string FakeLightningBoltImageUrl = "https://static.e2e-fixtures.local/lightning-bolt.png";
const string FakeElsaImageUrl = "https://static.e2e-fixtures.local/elsa.png";
const string FailingProviderCollectorNumber = "#FAIL";
const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04";
await using var container = new MsSqlBuilder(SqlServerImage).Build();
await container.StartAsync();

// A database of its own rather than the container's default master, so it can run under
// production's isolation level. Azure SQL Database has READ_COMMITTED_SNAPSHOT on; the container
// has it off, and without it two pickers recording picks at the same moment deadlocked on the
// status derivation's reads of OrderLines: a failure production cannot have, which failed the
// suite whenever two specs picked at once. The integration tests do the same
// (SqlServerDatabaseLease).
const string E2EDatabaseName = "LootSinglesE2E";
await using (var master = new SqlConnection(container.GetConnectionString()))
{
    await master.OpenAsync();
    await using var create = master.CreateCommand();
    create.CommandText =
        $"CREATE DATABASE [{E2EDatabaseName}]; "
        + $"ALTER DATABASE [{E2EDatabaseName}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;";
    await create.ExecuteNonQueryAsync();
}
var e2eConnectionString = new SqlConnectionStringBuilder(container.GetConnectionString())
{
    InitialCatalog = E2EDatabaseName,
}.ConnectionString;

var builder = WebApplication.CreateBuilder(args);

// Dedicated E2E port, deliberately not the dev API's 5098/7166, so the Playwright suite and
// scripts/start-dev.ps1 can run at the same time against separate servers and databases.
builder.WebHost.UseUrls("http://127.0.0.1:5199");

builder.Services.AddDbContext<LootSinglesDbContext>(options =>
    options.UseSqlServer(e2eConnectionString)
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
builder.Services.AddScoped<IPackingSlipSlicer, PdfPigPackingSlipSlicer>();
builder.Services.AddScoped<PackingSlipImportService>();
builder.Services.AddScoped<IPackingSlipImportService, ObservableProgressImportService>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IPackingRepository, PackingRepository>();
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
builder.Services.AddScoped<IPickingRepository, PickingRepository>();
builder.Services.AddScoped<PickingService>();
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
await WarmUpAsync(app.Services);
await app.RunAsync();

/// <summary>
/// Compiles the queries the first request would otherwise pay for, before Playwright starts.
/// </summary>
/// <remarks>
/// EF builds and compiles a query the first time it is used, and the status derivation is a
/// correlated subquery inside an ExecuteUpdate — the most expensive shape in this application.
/// Paid on the first pick of a run, it pushed that request past the suite's assertion timeout, so
/// whichever spec happened to pick first failed and the failure moved between runs. Paying it here
/// means the host is ready when it says it is, rather than merely responding (branch review
/// BR-004).
/// </remarks>
static async Task WarmUpAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();

    // Matches no row by construction, so it compiles the statement without touching data.
    await context
        .Orders.Where(order => order.Id == -1)
        .ExecuteUpdateAsync(setters =>
            setters.SetProperty(order => order.Status, order => order.Status)
        );

    await context.Orders.AsNoTracking().Where(order => order.Id == -1).ToListAsync();
}

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
    context.Employees.Add(
        new Employee
        {
            Username = "e2epicker",
            NormalizedUsername = "E2EPICKER",
            DisplayName = "E2E Picker",
            PinHash = pinHasher.Hash("1234"),
            Role = EmployeeRole.Picker,
            CreatedAt = DateTimeOffset.UtcNow,
        }
    );
    context.Employees.Add(
        new Employee
        {
            // A distinct second Picker so parallel claiming E2E tests never contend over
            // FR-009's one-active-claim-per-employee rule against a shared identity.
            Username = "e2epickertwo",
            NormalizedUsername = "E2EPICKERTWO",
            DisplayName = "E2E Picker Two",
            PinHash = pinHasher.Hash("1234"),
            Role = EmployeeRole.Picker,
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
    context.Orders.Add(
        new Order
        {
            TcgplayerOrderId = "E2E-ORDER-00002",
            Status = OrderStatus.Ready,
            // Deliberately older than E2E-ORDER-00001 so "Pick Next Order" (FIFO-oldest) always
            // prefers this order first and never disturbs E2E-ORDER-00001, which order-detail.spec.ts
            // depends on staying Ready/unclaimed.
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
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
                    Quantity = 1,
                },
            ],
        }
    );
    context.Orders.Add(
        new Order
        {
            TcgplayerOrderId = "E2E-ORDER-00003",
            Status = OrderStatus.Ready,
            // Older than E2E-ORDER-00001 but newer than E2E-ORDER-00002 — see the comment there.
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
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
                    Quantity = 1,
                },
            ],
        }
    );
    // 015-pick-completion: dedicated orders for pick-completion E2E, imported newer than the
    // others so "Pick Next Order" (FIFO-oldest) never hands them to another spec.
    context.Employees.Add(
        new Employee
        {
            Username = "e2epickerthree",
            NormalizedUsername = "E2EPICKERTHREE",
            DisplayName = "E2E Picker Three",
            PinHash = pinHasher.Hash("1234"),
            Role = EmployeeRole.Picker,
            CreatedAt = DateTimeOffset.UtcNow,
        }
    );
    context.Employees.Add(
        new Employee
        {
            Username = "e2epickerfour",
            NormalizedUsername = "E2EPICKERFOUR",
            DisplayName = "E2E Picker Four",
            PinHash = pinHasher.Hash("1234"),
            Role = EmployeeRole.Picker,
            CreatedAt = DateTimeOffset.UtcNow,
        }
    );
    context.Orders.Add(
        new Order
        {
            TcgplayerOrderId = "E2E-ORDER-00004",
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(10),
            OrderLines = [PickCompletionLine("Charizard", 2), PickCompletionLine("Blastoise", 1)],
        }
    );
    context.Orders.Add(
        new Order
        {
            TcgplayerOrderId = "E2E-ORDER-00005",
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(15),
            OrderLines = [PickCompletionLine("Venusaur", 1), PickCompletionLine("Mewtwo", 1)],
        }
    );
    // 016-mobile-picking T034: a dedicated picker and order for the focused-view spec, so it
    // never contends with another spec's claim while workers run in parallel.
    context.Employees.Add(
        new Employee
        {
            Username = "e2epickerfive",
            NormalizedUsername = "E2EPICKERFIVE",
            DisplayName = "E2E Picker Five",
            PinHash = pinHasher.Hash("1234"),
            Role = EmployeeRole.Picker,
            CreatedAt = DateTimeOffset.UtcNow,
        }
    );
    // Two sets, the first holding two products, so the spec can move within a box, trip the
    // unfinished-box guard, and then cross a finished-box boundary.
    context.Orders.Add(
        new Order
        {
            TcgplayerOrderId = "E2E-ORDER-00007",
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(25),
            OrderLines =
            [
                SetAwareLine("Pokemon", "Aaa Set", "First Card", "#001/100", 1),
                SetAwareLine("Pokemon", "Aaa Set", "Second Card", "#002/100", 2),
                SetAwareLine("Pokemon", "Bbb Set", "Third Card", "#003/100", 1),
            ],
        }
    );
    // 016-mobile-picking T051: two more pickers and their own order for the claiming spec, so
    // it never contends with another spec's claim while workers run in parallel.
    foreach (
        var (username, displayName) in new[]
        {
            ("e2epickersix", "E2E Picker Six"),
            ("e2epickerseven", "E2E Picker Seven"),
            ("e2epickereight", "E2E Picker Eight"),
            ("e2epickernine", "E2E Picker Nine"),
            ("e2epickerten", "E2E Picker Ten"),
            ("e2epickereleven", "E2E Picker Eleven"),
            ("e2epickertwelve", "E2E Picker Twelve"),
            ("e2epickerthirteen", "E2E Picker Thirteen"),
            ("e2epickerfourteen", "E2E Picker Fourteen"),
            ("e2epickerfifteen", "E2E Picker Fifteen"),
        }
    )
    {
        context.Employees.Add(
            new Employee
            {
                Username = username,
                NormalizedUsername = username.ToUpperInvariant(),
                DisplayName = displayName,
                PinHash = pinHasher.Hash("1234"),
                Role = EmployeeRole.Picker,
                CreatedAt = DateTimeOffset.UtcNow,
            }
        );
    }
    context.Orders.Add(
        new Order
        {
            TcgplayerOrderId = "E2E-ORDER-00008",
            Status = OrderStatus.Ready,
            // Newer than every other seeded order so "Pick Next Order" never selects it.
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(30),
            OrderLines =
            [
                SetAwareLine("Pokemon", "Base Set", "Claimable Card", "#001/102", 1),
                SetAwareLine("Pokemon", "Base Set", "Second Claimable", "#002/102", 2),
            ],
        }
    );
    // 016-mobile-picking T002. Spans two games with two sets each, so set-aware picking can be
    // exercised end to end: game grouping, set ordering within a game, a line with no recorded
    // set, and a quantity greater than one.
    //
    // The lines are deliberately listed in an order matching neither the expected game order nor
    // the expected set order, so a grouping test cannot pass by accident of input order.
    //
    // Newer than every other seeded order so "Pick Next Order" (FIFO-oldest) never selects it.
    context.Orders.Add(
        new Order
        {
            TcgplayerOrderId = "E2E-ORDER-00006",
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(20),
            OrderLines =
            [
                SetAwareLine("Pokemon", "Surging Sparks", "Pikachu ex", "#238/191", 1),
                SetAwareLine("Magic", "Bloomburrow", "Mabel", "#213", 1),
                // Quantity greater than one — the high-risk case (PRD §15).
                SetAwareLine("Pokemon", "Black Bolt", "Genesect ex", "#067/086", 3),
                SetAwareLine("Magic", "Aetherdrift", "Hare Apparent", "#124", 1),
                // No recorded set. Must remain visible and pickable rather than being grouped
                // out of existence (spec FR-005, Constitution V).
                SetAwareLine("Pokemon", "", "Mystery Promo", "#PR-01", 1),
            ],
        }
    );
    // 017-pick-completion-handoff T030: one order that finishes clean and one that ends held,
    // so quickstart scenarios 1 and 2 each get an order nothing else touches. Both are newer
    // than every other seeded order so "Pick Next Order" never selects them.
    context.Orders.Add(
        new Order
        {
            TcgplayerOrderId = "E2E-ORDER-00009",
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(35),
            OrderLines =
            [
                SetAwareLine("Pokemon", "Handoff Set", "Handoff First", "#001/050", 3),
                SetAwareLine("Pokemon", "Handoff Set", "Handoff Second", "#002/050", 5),
            ],
        }
    );
    context.Orders.Add(
        new Order
        {
            TcgplayerOrderId = "E2E-ORDER-00010",
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(40),
            OrderLines =
            [
                SetAwareLine("Pokemon", "Hold Set", "Hold Pulled", "#001/050", 7),
                SetAwareLine("Pokemon", "Hold Set", "Hold Missing", "#002/050", 1),
            ],
        }
    );
    // 017 T060: two orders for the packing desk spec — one packed clean, one blocked by an issue.
    context.Orders.Add(
        new Order
        {
            TcgplayerOrderId = "E2E-ORDER-00011",
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(45),
            OrderLines = [SetAwareLine("Pokemon", "Desk Set", "Desk Packable", "#001/050", 2)],
        }
    );
    context.Orders.Add(
        new Order
        {
            TcgplayerOrderId = "E2E-ORDER-00012",
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(50),
            OrderLines = [SetAwareLine("Pokemon", "Desk Set", "Desk Blocker", "#002/050", 1)],
        }
    );
    // 017 T064/T068: the reprint and awaiting-packing-count spec, with an order of its own so
    // the dashboard number it watches is not moved by another worker mid-assertion.
    context.Orders.Add(
        new Order
        {
            TcgplayerOrderId = "E2E-ORDER-00013",
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(55),
            OrderLines = [SetAwareLine("Pokemon", "Reprint Set", "Reprint Card", "#003/050", 1)],
        }
    );
    // 017 T101: the order "Next order" hands to pick-handoff's first test. Pick Next itself takes
    // the oldest Ready order, which other specs own, so that test steers the choice here.
    context.Orders.Add(
        new Order
        {
            TcgplayerOrderId = "E2E-ORDER-00014",
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(60),
            OrderLines = [SetAwareLine("Pokemon", "Second Set", "Second Sleeve", "#004/050", 1)],
        }
    );
    // 017 T108: finishing with a product never looked at ends held, not complete.
    context.Orders.Add(
        new Order
        {
            TcgplayerOrderId = "E2E-ORDER-00015",
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(65),
            OrderLines =
            [
                SetAwareLine("Pokemon", "Skip Set", "Skip Pulled", "#001/050", 2),
                SetAwareLine("Pokemon", "Skip Set", "Skip Untouched", "#002/050", 1),
            ],
        }
    );
    // 018 T001: the reported-issue card spec, on a phone. Quantity 4 is the case the feature exists
    // for (a dock that read "Pulled all 4" on a product reported as 3 of 4); quantity 1 is the
    // found action's other wording.
    context.Orders.Add(
        new Order
        {
            TcgplayerOrderId = "E2E-ORDER-00016",
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(70),
            OrderLines =
            [
                SetAwareLine("Pokemon", "Issue Set", "Issue Card Four", "#001/050", 4),
                SetAwareLine("Pokemon", "Issue Set", "Issue Card One", "#002/050", 1),
            ],
        }
    );
    // 018 T024: the desktop issue-panel spec. A quantity-4 product for counts, a single card for a
    // report with no counts, and a clean product that must keep its ordinary row throughout.
    context.Orders.Add(
        new Order
        {
            TcgplayerOrderId = "E2E-ORDER-00017",
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(75),
            OrderLines =
            [
                SetAwareLine("Pokemon", "Panel Set", "Panel Card Four", "#001/050", 4),
                SetAwareLine("Pokemon", "Panel Set", "Panel Card Damaged", "#002/050", 1),
                SetAwareLine("Pokemon", "Panel Set", "Panel Card Clean", "#003/050", 1),
            ],
        }
    );
    await context.SaveChangesAsync();

    static OrderLine SetAwareLine(
        string productLine,
        string set,
        string productName,
        string collectorNumber,
        int quantity
    ) =>
        new()
        {
            RawDescription = $"{productName} - {set} - {collectorNumber} - Rare - Near Mint",
            ProductLine = productLine,
            ProductName = productName,
            Set = set,
            CollectorNumber = collectorNumber,
            Condition = "Near Mint",
            Quantity = quantity,
        };

    static OrderLine PickCompletionLine(string productName, int quantity) =>
        new()
        {
            RawDescription = $"{productName} - Base Set - #1/102 - Rare - Near Mint",
            ProductLine = "Pokemon",
            ProductName = productName,
            Set = "Base Set",
            CollectorNumber = "#1/102",
            Condition = "Near Mint",
            Quantity = quantity,
        };
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
