using System.Text.Json;
using System.Text.Json.Serialization;
using LootSingles.Api.Controllers;
using LootSingles.Application.Auth;
using LootSingles.Application.Dashboard;
using LootSingles.Application.Import;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Import;
using LootSingles.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5098");

var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
builder.Services.AddSingleton(connection);
builder.Services.AddDbContext<LootSinglesDbContext>(options => options.UseSqlite(connection));

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
builder.Services.AddScoped<IPackingSlipImportService, PackingSlipImportService>();

// IOrderRepository/OrderRepository and OrdersService are registered by Phase 4 (US2) T027 once
// those Application/Infrastructure types exist (specs/004-order-import-ui/tasks.md T022-T025).
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
app.MapGet("/health", () => Results.Ok());

await SeedAsync(app.Services);
await app.RunAsync();

static async Task SeedAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
    var pinHasher = scope.ServiceProvider.GetRequiredService<IPinHasher>();
    await context.Database.EnsureCreatedAsync();
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
            ],
        }
    );
    await context.SaveChangesAsync();
}
