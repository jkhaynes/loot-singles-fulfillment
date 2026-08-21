using System.Net;
using System.Net.Http.Json;
using LootSingles.Api.Controllers;
using LootSingles.Domain.Employees;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LootSingles.IntegrationTests.Auth;

public class AuthControllerTests
{
    [Fact]
    public async Task Login_CorrectCredentials_ReturnsOkSetsCookieAndMeReturnsIdentity()
    {
        await using var factory = new AuthWebApplicationFactory();
        var employee = await SeedEmployeeAsync(factory, "jsmith", "1234", isActive: true);
        using var client = factory.CreateAuthenticatedClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("jsmith", "1234"));

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var body = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.Equal(employee.Id, body?.EmployeeId);
        Assert.Equal("jsmith", body?.DisplayName);
        Assert.Equal(EmployeeRole.Picker.ToString(), body?.Role);

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var meBody = await meResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.Equal(employee.Id, meBody?.EmployeeId);
    }

    [Fact]
    public async Task Login_WrongPin_ReturnsUnauthorizedWithGenericBody()
    {
        await using var factory = new AuthWebApplicationFactory();
        await SeedEmployeeAsync(factory, "jsmith", "1234", isActive: true);
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("jsmith", "9999"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("invalid_credentials", body?.Error);
    }

    [Fact]
    public async Task Login_NonexistentUsername_ReturnsIdenticalUnauthorizedBodyAsWrongPin()
    {
        await using var factory = new AuthWebApplicationFactory();
        await SeedEmployeeAsync(factory, "jsmith", "1234", isActive: true);
        using var client = factory.CreateAuthenticatedClient();

        var wrongPinResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("jsmith", "9999"));
        var nonexistentResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("ghost", "1234"));

        Assert.Equal(HttpStatusCode.Unauthorized, nonexistentResponse.StatusCode);
        var wrongPinBody = await wrongPinResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        var nonexistentBody = await nonexistentResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal(wrongPinBody?.Error, nonexistentBody?.Error);
        Assert.Equal(wrongPinBody?.Message, nonexistentBody?.Message);
    }

    [Fact]
    public async Task Login_DeactivatedAccount_ReturnsGenericUnauthorizedNotLocked()
    {
        await using var factory = new AuthWebApplicationFactory();
        await SeedEmployeeAsync(factory, "jsmith", "1234", isActive: false);
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("jsmith", "1234"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("invalid_credentials", body?.Error);
    }

    [Fact]
    public async Task Login_ThresholdAttemptReturnsUnauthorizedThenSubsequentCorrectPinReturnsLocked()
    {
        await using var factory = new AuthWebApplicationFactory();
        await SeedEmployeeAsync(factory, "jsmith", "1234", isActive: true);
        using var client = factory.CreateAuthenticatedClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failedResponse = await client.PostAsJsonAsync(
                "/api/auth/login", new LoginRequest("jsmith", "9999"));
            Assert.Equal(HttpStatusCode.Unauthorized, failedResponse.StatusCode);
        }

        var lockedResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest("jsmith", "1234"));

        Assert.Equal((HttpStatusCode)423, lockedResponse.StatusCode);
        var body = await lockedResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("account_locked", body?.Error);
        Assert.Equal("This account is locked. Ask a Manager/Admin to unlock it.", body?.Message);
    }

    [Fact]
    public async Task Login_SuccessBelowThresholdResetsCountForFutureAttempts()
    {
        await using var factory = new AuthWebApplicationFactory();
        var employee = await SeedEmployeeAsync(factory, "jsmith", "1234", isActive: true);
        using var client = factory.CreateAuthenticatedClient();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var failedResponse = await client.PostAsJsonAsync(
                "/api/auth/login", new LoginRequest("jsmith", "9999"));
            Assert.Equal(HttpStatusCode.Unauthorized, failedResponse.StatusCode);
        }

        var successfulResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest("jsmith", "1234"));

        Assert.Equal(HttpStatusCode.OK, successfulResponse.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        var persistedEmployee = await context.Employees.SingleAsync(candidate => candidate.Id == employee.Id);
        Assert.Equal(0, persistedEmployee.FailedAttemptCount);
        Assert.False(persistedEmployee.IsLocked);
    }

    [Fact]
    public async Task Login_MalformedPin_ReturnsBadRequest()
    {
        await using var factory = new AuthWebApplicationFactory();
        await SeedEmployeeAsync(factory, "jsmith", "1234", isActive: true);
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("jsmith", "12a"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Me_NoSession_ReturnsUnauthorized()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.EnsureDatabaseCreatedAsync();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<Employee> SeedEmployeeAsync(
        AuthWebApplicationFactory factory, string username, string pin, bool isActive)
    {
        var hasher = new Pbkdf2PinHasher();
        var employee = new Employee
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = username,
            PinHash = hasher.Hash(pin),
            Role = EmployeeRole.Picker,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await factory.SeedAsync(context =>
        {
            context.Employees.Add(employee);
            return Task.CompletedTask;
        });

        return employee;
    }
}
