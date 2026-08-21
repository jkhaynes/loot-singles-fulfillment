using LootSingles.Application.Auth;
using LootSingles.Application.Import;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Import;
using LootSingles.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddDbContext<LootSinglesDbContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("LootSingles")
        ?? throw new InvalidOperationException("Connection string 'LootSingles' is required.")));
builder.Services.AddScoped<IImportPersistence>(provider =>
    provider.GetRequiredService<LootSinglesDbContext>());
builder.Services.AddScoped<IPackingSlipParser, PdfPigPackingSlipParser>();
builder.Services.AddScoped<IPackingSlipImportService, PackingSlipImportService>();

builder.Services.AddScoped<IPinHasher, Pbkdf2PinHasher>();
builder.Services.AddScoped<IEmployeeRepository, EmployeeRepository>();
var lockoutOptions = builder.Configuration
    .GetSection(LockoutOptions.SectionName)
    .Get<LockoutOptions>() ?? new LockoutOptions();
if (lockoutOptions.FailedAttemptThreshold < 1)
{
    throw new InvalidOperationException("Authentication lockout threshold must be at least 1.");
}
builder.Services.AddSingleton(lockoutOptions);
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<EmployeeManagementService>();
builder.Services.AddScoped<EmployeeSessionCookieEvents>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
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

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposed so WebApplicationFactory<Program> in the integration test project can host this app in-process.
public partial class Program;
