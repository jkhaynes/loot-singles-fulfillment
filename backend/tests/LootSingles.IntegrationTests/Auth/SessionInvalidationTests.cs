using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using LootSingles.Api.Controllers;
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
    public async Task Deactivation_ViaRealEndpoint_InvalidatesAlreadyActiveSessionOnNextRequest()
    {
        await using var factory = new AuthWebApplicationFactory();
        var target = await SeedEmployeeAsync(factory, "sessiontarget", "1234", EmployeeRole.Picker);
        await SeedEmployeeAsync(factory, "sessionmanager", "1234", EmployeeRole.ManagerAdmin);

        using var targetClient = factory.CreateAuthenticatedClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await targetClient.PostAsJsonAsync(
                    "/api/auth/login",
                    new LoginRequest("sessiontarget", "1234")
                )
            ).StatusCode
        );
        Assert.Equal(HttpStatusCode.OK, (await targetClient.GetAsync("/api/auth/me")).StatusCode);

        using var managerClient = factory.CreateAuthenticatedClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await managerClient.PostAsJsonAsync(
                    "/api/auth/login",
                    new LoginRequest("sessionmanager", "1234")
                )
            ).StatusCode
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await managerClient.PostAsync($"/api/employees/{target.Id}/deactivate", null)
            ).StatusCode
        );

        var response = await targetClient.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<Employee> SeedEmployeeAsync(
        AuthWebApplicationFactory factory,
        string username,
        string pin,
        EmployeeRole role
    )
    {
        var hasher = new Pbkdf2PinHasher();
        var employee = new Employee
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = username,
            PinHash = hasher.Hash(pin),
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await factory.SeedAsync(context =>
        {
            context.Employees.Add(employee);
            return Task.CompletedTask;
        });

        return employee;
    }

    [Fact]
    public async Task ValidatePrincipal_EmployeeDeactivatedSinceIssuance_RejectsThePrincipal()
    {
        var connectionString = $"Data Source=session-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using var context = new LootSinglesDbContext(
            new DbContextOptionsBuilder<LootSinglesDbContext>().UseSqlite(connectionString).Options
        );
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
            new DbContextOptionsBuilder<LootSinglesDbContext>().UseSqlite(connectionString).Options
        );
        await context.Database.EnsureCreatedAsync();

        var employee = NewEmployee(isActive: true);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var result = await ValidateAsync(context, employee.Id);

        Assert.NotNull(result.Principal);
    }

    private static async Task<CookieValidatePrincipalContext> ValidateAsync(
        LootSinglesDbContext context,
        int employeeId
    )
    {
        var services = new ServiceCollection();
        services.AddScoped<IEmployeeRepository>(_ => new EmployeeRepository(context));
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, employeeId.ToString())],
                CookieAuthenticationDefaults.AuthenticationScheme
            )
        );
        var ticket = new AuthenticationTicket(
            principal,
            CookieAuthenticationDefaults.AuthenticationScheme
        );
        var scheme = new AuthenticationScheme(
            CookieAuthenticationDefaults.AuthenticationScheme,
            null,
            typeof(CookieAuthenticationHandler)
        );
        var validateContext = new CookieValidatePrincipalContext(
            httpContext,
            scheme,
            new CookieAuthenticationOptions(),
            ticket
        );

        await new EmployeeSessionCookieEvents().ValidatePrincipal(validateContext);
        return validateContext;
    }

    private static Employee NewEmployee(bool isActive) =>
        new()
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
