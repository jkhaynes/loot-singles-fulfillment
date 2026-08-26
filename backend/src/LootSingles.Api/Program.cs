using System.Text.Json;
using System.Text.Json.Serialization;
using LootSingles.Api;
using LootSingles.Api.Controllers;
using LootSingles.Application.Auth;
using LootSingles.Application.CardCatalog;
using LootSingles.Application.Dashboard;
using LootSingles.Application.Import;
using LootSingles.Application.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.CardCatalog;
using LootSingles.Infrastructure.Import;
using LootSingles.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// The Order Import UI feature (004) relies on this camelCase property/enum casing for both
// OrdersController's model-bound responses and ImportsController's manually-written NDJSON lines
// (which reuse these same JsonSerializerOptions via IOptions<JsonOptions>), so the wire format
// matches contracts/import-api.md and contracts/orders-api.md (e.g. "unreadablePdf", "ready").
builder
    .Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        );
    });
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = ImportsController.MaximumFileBytes + 1_048_576
);
var databaseConnectionString = builder.Configuration.GetConnectionString("LootSingles");
if (string.IsNullOrWhiteSpace(databaseConnectionString))
{
    throw new InvalidOperationException(
        "Configuration key 'ConnectionStrings:LootSingles' is required."
    );
}
builder.Services.AddDbContext<LootSinglesDbContext>(options =>
    options.UseSqlServer(databaseConnectionString)
);
builder.Services.AddScoped<IImportPersistence, ImportRepository>();
builder.Services.AddScoped<IPackingSlipParser, PdfPigPackingSlipParser>();
builder.Services.AddScoped<IPackingSlipImportService, PackingSlipImportService>();

builder.Services.AddScoped<IPinHasher, Pbkdf2PinHasher>();
builder.Services.AddScoped<IEmployeeRepository, EmployeeRepository>();
var lockoutOptions =
    builder.Configuration.GetSection(LockoutOptions.SectionName).Get<LockoutOptions>()
    ?? new LockoutOptions();
if (lockoutOptions.FailedAttemptThreshold < 1)
{
    throw new InvalidOperationException("Authentication lockout threshold must be at least 1.");
}
builder.Services.AddSingleton(lockoutOptions);
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<EmployeeManagementService>();
builder.Services.AddScoped<BootstrapAdminService>();
builder.Services.AddScoped<BootstrapAdminCommand>();
builder.Services.AddScoped<EmployeeSessionCookieEvents>();
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddHttpClient<TcgdexCardCatalogProvider>(client =>
{
    client.BaseAddress = new Uri("https://api.tcgdex.net/v2/en/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient(
    "tcgdex",
    client =>
    {
        client.BaseAddress = new Uri("https://api.tcgdex.net/v2/en/");
        client.Timeout = TimeSpan.FromSeconds(10);
    }
);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TcgdexSetCatalog>();
builder.Services.AddScoped<ICardCatalogProvider>(sp =>
    sp.GetRequiredService<TcgdexCardCatalogProvider>()
);
builder.Services.AddHttpClient<ScryfallCardCatalogProvider>(client =>
{
    client.BaseAddress = new Uri("https://api.scryfall.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("LootSinglesFulfillment/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddSingleton<IMagicSetCrosswalk>(_ => MagicSetCrosswalk.LoadEmbedded());
builder.Services.AddScoped<ICardCatalogProvider>(sp =>
    sp.GetRequiredService<ScryfallCardCatalogProvider>()
);
builder.Services.AddScoped<CardImageEnrichmentService>();
builder.Services.AddScoped<OrdersService>();
builder
    .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
        options.EventsType = typeof(EmployeeSessionCookieEvents);
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Validate the checked-in crosswalk during startup rather than on the first order-detail request.
_ = app.Services.GetRequiredService<IMagicSetCrosswalk>();

if (args.Length == 1 && string.Equals(args[0], "bootstrap-admin", StringComparison.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var command = scope.ServiceProvider.GetRequiredService<BootstrapAdminCommand>();
    Environment.ExitCode = await command.ExecuteAsync(Console.Out, CancellationToken.None);
    return;
}

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposed so WebApplicationFactory<Program> in the integration test project can host this app in-process.
public partial class Program;
