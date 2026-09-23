using System.Text.Json;
using System.Text.Json.Serialization;
using LootSingles.Api;
using LootSingles.Api.Controllers;
using LootSingles.Application.Auth;
using LootSingles.Application.CardCatalog;
using LootSingles.Application.Dashboard;
using LootSingles.Application.Import;
using LootSingles.Application.Orders;
using LootSingles.Application.Packing;
using LootSingles.Application.Picking;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.CardCatalog;
using LootSingles.Infrastructure.Import;
using LootSingles.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
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
builder.Services.AddScoped<IPackingSlipSlicer, PdfPigPackingSlipSlicer>();
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
builder.Services.AddScoped<MigrateCommand>();
builder.Services.AddScoped<EmployeeSessionCookieEvents>();
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
builder.Services.AddScoped<IPackingRepository, PackingRepository>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IPickingRepository, PickingRepository>();
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
builder.Services.AddHttpClient(
    "lorcast",
    client =>
    {
        client.BaseAddress = new Uri("https://api.lorcast.com/v0/");
        client.Timeout = TimeSpan.FromSeconds(10);
    }
);
builder.Services.AddHttpClient<LorcastCardCatalogProvider>(client =>
{
    client.BaseAddress = new Uri("https://api.lorcast.com/v0/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<LorcastSetCatalog>();
builder.Services.AddScoped<ICardCatalogProvider>(sp =>
    sp.GetRequiredService<LorcastCardCatalogProvider>()
);
builder.Services.AddScoped<CardImageEnrichmentService>();
builder.Services.AddScoped<OrdersService>();
builder.Services.AddScoped<OrderClaimService>();
builder.Services.AddScoped<PickingService>();

// 019 T035 / research.md §5 / FR-026. Data Protection encrypts the session cookie, and its key ring
// defaults to memory. The container scales to zero when idle — that is what keeps it inside the free
// compute grant — so an in-memory key ring would sign every picker out after any quiet spell, not
// just across a release. Persisting to the application's own database keeps sessions alive without a
// paid Key Vault.
//
// SetApplicationName is a constant on purpose: changing it invalidates every active session. Each
// environment has its own database and therefore its own key ring, so a stage cookie is never valid
// in production.
builder
    .Services.AddDataProtection()
    .SetApplicationName("LootSinglesFulfillment")
    .PersistKeysToDbContext<LootSinglesDbContext>();

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

// 019 T012. Applies pending migrations and exits without ever building the HTTP pipeline, so the
// migrate job never starts a listener (contracts/deployment.md). The same image serves HTTP with no
// argument, migrates with this one, and bootstraps with the one above.
if (args.Length == 1 && string.Equals(args[0], "migrate", StringComparison.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var command = scope.ServiceProvider.GetRequiredService<MigrateCommand>();
    Environment.ExitCode = await command.ExecuteAsync(Console.Out, CancellationToken.None);
    return;
}

// Configure the HTTP request pipeline.

// 019 T007 / research.md §2. Azure Container Apps terminates TLS at its ingress and forwards plain
// HTTP to the container, so UseHttpsRedirection below would redirect a request the ingress already
// served over HTTPS — which the ingress serves back over HTTPS, which redirects again. Honouring
// X-Forwarded-Proto breaks that loop.
//
// KnownIPNetworks and KnownProxies are cleared deliberately. The middleware ignores forwarded headers
// from callers it does not recognise, and the Container Apps ingress is not on a recognised private
// network, so leaving the defaults in place means the header is silently dropped and the loop
// returns. Trusting the header is safe *here* because nothing but the environment's own ingress can
// route to the container's port: the app listens only inside the Container Apps environment, and
// that environment lives in our own subnet. If this application is ever exposed directly to a
// network where arbitrary callers can reach its port, this block must be revisited — an attacker
// who can set X-Forwarded-Proto could otherwise have a plain-HTTP request treated as secure.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto,
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

app.UseHttpsRedirection();

// 019 T009 / FR-004. One container serves the API and the built web app from a single origin,
// because the session cookie is SameSite=Strict and a browser will not send it across origins.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// 019 T008 / contracts/health-api.md. Liveness only: "is this process serving HTTP?" It MUST NOT
// touch the database. The platform replaces a container whose probe fails, and restarting cannot
// fix a database problem — it only destroys an application that could still serve its sign-in page
// and log a useful error (FR-023). /health/database answers the database question instead.
app.MapGet("/health", () => Results.Ok()).AllowAnonymous();

// 019 T048 / contracts/health-api.md / FR-024, FR-025. Proves the *application's* identity can read
// from the database — which a successful migration does not, because the migrate job runs under a
// different identity (research.md §7). Called by production's deploy smoke test only, and never by
// the container probe.
//
// 019 T051 / BR-001. Registered only where an environment opts in, and absent by default. The
// endpoint is anonymous and touches the database, so on stage — whose free-tier database auto-pauses
// — every call from anywhere on the internet wakes it and spends about an hour of a ~55-hour monthly
// allowance. Left always-on, background scanning alone could exhaust that and make stage unusable
// until the 1st. Production sets the flag because its release checks need the proof and its database
// is always awake; stage does not set it and deploy-stage.yml does not ask.
if (builder.Configuration.GetValue<bool>("HealthChecks:ExposeDatabaseEndpoint"))
{
    app.MapGet(
            "/health/database",
            async (
                LootSinglesDbContext database,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken
            ) =>
            {
                try
                {
                    _ = await database.Employees.AnyAsync(cancellationToken);
                    return Results.Ok();
                }
                catch (Exception exception)
                {
                    // The reason goes to the operator, never to the caller: an anonymous caller learns
                    // only that the database is unreachable, which a 500 on the sign-in page already
                    // reveals. Server names, the identity's client id and exception detail stay out of
                    // the response body (FR-025).
                    loggerFactory
                        .CreateLogger("LootSingles.Api.HealthDatabase")
                        .LogError(exception, "Database health check failed.");
                    return Results.Text(
                        "Database unavailable.",
                        statusCode: StatusCodes.Status503ServiceUnavailable
                    );
                }
            }
        )
        .AllowAnonymous();
}

app.MapControllers();

// 019 T009 / FR-004. Order matters: an unmatched /api route must return a real 404, never the web
// app's HTML with status 200. Without this first fallback, a client expecting JSON fails on parse
// rather than on status, and a test asserting 404 passes for the wrong reason.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

// Exposed so WebApplicationFactory<Program> in the integration test project can host this app in-process.
public partial class Program;
