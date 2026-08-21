using System.Security.Claims;
using LootSingles.Application.Auth;
using LootSingles.Domain.Employees;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LootSingles.IntegrationTests.Auth;

public class SessionInvalidationTests
{
    [Fact]
    public async Task ValidatePrincipal_EmployeeDeactivatedSinceIssuance_RejectsThePrincipal()
    {
        var connectionString = $"Data Source=session-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using var context = new LootSinglesDbContext(
            new DbContextOptionsBuilder<LootSinglesDbContext>().UseSqlite(connectionString).Options);
        await context.Database.EnsureCreatedAsync();

        var employee = NewEmployee(isActive: false);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var result = await ValidateAsync(context, employee.Id);

        Assert.Null(result.Principal);
    }

    [Fact]
    public async Task ValidatePrincipal_EmployeeStillActive_AcceptsThePrincipal()
    {
        var connectionString = $"Data Source=session-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using var context = new LootSinglesDbContext(
            new DbContextOptionsBuilder<LootSinglesDbContext>().UseSqlite(connectionString).Options);
        await context.Database.EnsureCreatedAsync();

        var employee = NewEmployee(isActive: true);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var result = await ValidateAsync(context, employee.Id);

        Assert.NotNull(result.Principal);
    }

    private static async Task<CookieValidatePrincipalContext> ValidateAsync(LootSinglesDbContext context, int employeeId)
    {
        var services = new ServiceCollection();
        services.AddScoped<IEmployeeRepository>(_ => new EmployeeRepository(context));
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, employeeId.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme));
        var ticket = new AuthenticationTicket(principal, CookieAuthenticationDefaults.AuthenticationScheme);
        var scheme = new AuthenticationScheme(
            CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        var validateContext = new CookieValidatePrincipalContext(
            httpContext, scheme, new CookieAuthenticationOptions(), ticket);

        await new EmployeeSessionCookieEvents().ValidatePrincipal(validateContext);
        return validateContext;
    }

    private static Employee NewEmployee(bool isActive) => new()
    {
        Username = "jsmith",
        NormalizedUsername = "JSMITH",
        DisplayName = "Jamie Smith",
        PinHash = "hash",
        Role = EmployeeRole.Picker,
        IsActive = isActive,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
