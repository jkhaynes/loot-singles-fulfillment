using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LootSingles.Api.Controllers;
using LootSingles.Domain.Employees;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LootSingles.IntegrationTests.Auth;

public class EmployeesControllerTests
{
    [Fact]
    public async Task Manager_CanCreateListAndReadAuditEvents()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsAsync(factory, EmployeeRole.ManagerAdmin);

        var create = await client.PostAsJsonAsync(
            "/api/employees",
            new
            {
                username = "newpicker",
                displayName = "New Picker",
                initialPin = "1234",
                role = "Picker",
            }
        );

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<CreatedEmployeeResponse>();
        Assert.True(created?.EmployeeId > 0);

        var list = await client.GetFromJsonAsync<List<EmployeeListResponse>>("/api/employees");
        Assert.Contains(
            list!,
            item => item.EmployeeId == created!.EmployeeId && item.Username == "newpicker"
        );

        var audit = await client.GetFromJsonAsync<List<AuditEventResponse>>(
            $"/api/employees/{created!.EmployeeId}/audit-events"
        );
        Assert.Contains(
            audit!,
            item =>
                item.ActionType == "AccountCreated" && item.TargetEmployeeId == created.EmployeeId
        );
    }

    [Fact]
    public async Task Create_DuplicateReturnsConflictAndMalformedInputReturnsBadRequest()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsAsync(factory, EmployeeRole.ManagerAdmin);
        var request = new
        {
            username = "newpicker",
            displayName = "New Picker",
            initialPin = "1234",
            role = "Picker",
        };
        Assert.Equal(
            HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/employees", request)).StatusCode
        );

        var duplicate = await client.PostAsJsonAsync(
            "/api/employees",
            new
            {
                username = "NEWPICKER",
                displayName = "Duplicate",
                initialPin = "1234",
                role = "Picker",
            }
        );
        var malformedPin = await client.PostAsJsonAsync(
            "/api/employees",
            new
            {
                username = "other",
                displayName = "Other",
                initialPin = "12ab",
                role = "Picker",
            }
        );

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(
            "username_taken",
            (await duplicate.Content.ReadFromJsonAsync<ErrorResponse>())?.Error
        );
        Assert.Equal(HttpStatusCode.BadRequest, malformedPin.StatusCode);
    }

    [Fact]
    public async Task Manager_CanDeactivateReactivateResetPinAndUnlock()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsAsync(factory, EmployeeRole.ManagerAdmin);
        var target = await SeedEmployeeAsync(factory, "target", "1234", EmployeeRole.Picker);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync($"/api/employees/{target.Id}/deactivate", null)).StatusCode
        );
        await SetLockoutAsync(factory, target.Id);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync($"/api/employees/{target.Id}/reactivate", null)).StatusCode
        );
        await AssertEmployeeStateAsync(
            factory,
            target.Id,
            isActive: true,
            isLocked: false,
            failedCount: 0
        );

        await SetLockoutAsync(factory, target.Id);
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    $"/api/employees/{target.Id}/reset-pin",
                    new { newPin = "4321" }
                )
            ).StatusCode
        );
        await AssertEmployeeStateAsync(
            factory,
            target.Id,
            isActive: true,
            isLocked: true,
            failedCount: 5
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync($"/api/employees/{target.Id}/unlock", null)).StatusCode
        );
        await AssertEmployeeStateAsync(
            factory,
            target.Id,
            isActive: true,
            isLocked: false,
            failedCount: 0
        );

        using var loginClient = factory.CreateAuthenticatedClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await loginClient.PostAsJsonAsync(
                    "/api/auth/login",
                    new LoginRequest("target", "4321")
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task Deactivate_LastActiveManagerAdmin_ReturnsConflictAndLeavesAccountActive()
    {
        await using var factory = new AuthWebApplicationFactory();
        var manager = await SeedEmployeeAsync(
            factory,
            "manager",
            "1234",
            EmployeeRole.ManagerAdmin
        );
        using var client = factory.CreateAuthenticatedClient();
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("manager", "1234"));

        var response = await client.PostAsync($"/api/employees/{manager.Id}/deactivate", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "would_remove_last_manager_admin",
            (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Error
        );
        await AssertEmployeeStateAsync(
            factory,
            manager.Id,
            isActive: true,
            isLocked: false,
            failedCount: 0
        );
        var auditEvents = await client.GetFromJsonAsync<List<AuditEventResponse>>(
            $"/api/employees/{manager.Id}/audit-events"
        );
        Assert.DoesNotContain(auditEvents!, item => item.ActionType == "Deactivated");
    }

    [Fact]
    public async Task MissingEmployeeActionsReturnNotFound()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsAsync(factory, EmployeeRole.ManagerAdmin);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsync("/api/employees/404/deactivate", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsync("/api/employees/404/reactivate", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await client.PostAsJsonAsync(
                    "/api/employees/404/reset-pin",
                    new { newPin = "1234" }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsync("/api/employees/404/unlock", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync("/api/employees/404/audit-events")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await client.PostAsJsonAsync(
                    "/api/employees/404/change-role",
                    new { role = "Picker" }
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task ChangeRole_ValidRequest_ReturnsUpdatedRoleAndInvalidatesTargetSession()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsAsync(factory, EmployeeRole.ManagerAdmin);
        var target = await SeedEmployeeAsync(factory, "target", "1234", EmployeeRole.Picker);

        var response = await client.PostAsJsonAsync(
            $"/api/employees/{target.Id}/change-role",
            new { role = "ManagerAdmin" }
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ChangeRoleResponse>();
        Assert.Equal(target.Id, body?.EmployeeId);
        Assert.Equal("ManagerAdmin", body?.Role);
        await AssertEmployeeRoleAsync(factory, target.Id, EmployeeRole.ManagerAdmin);
    }

    [Fact]
    public async Task ChangeRole_MissingOrInvalidRole_ReturnsBadRequest()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsAsync(factory, EmployeeRole.ManagerAdmin);
        var target = await SeedEmployeeAsync(factory, "target", "1234", EmployeeRole.Picker);

        var missing = await client.PostAsJsonAsync(
            $"/api/employees/{target.Id}/change-role",
            new { }
        );
        var invalid = await client.PostAsJsonAsync(
            $"/api/employees/{target.Id}/change-role",
            new { role = "SuperAdmin" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task ChangeRole_WouldRemoveLastManagerAdmin_ReturnsConflictAndLeavesRoleUnchanged()
    {
        await using var factory = new AuthWebApplicationFactory();
        var manager = await SeedEmployeeAsync(
            factory,
            "manager",
            "1234",
            EmployeeRole.ManagerAdmin
        );
        using var client = factory.CreateAuthenticatedClient();
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("manager", "1234"));

        var response = await client.PostAsJsonAsync(
            $"/api/employees/{manager.Id}/change-role",
            new { role = "Picker" }
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "would_remove_last_manager_admin",
            (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Error
        );
        await AssertEmployeeRoleAsync(factory, manager.Id, EmployeeRole.ManagerAdmin);
        var auditEvents = await client.GetFromJsonAsync<List<AuditEventResponse>>(
            $"/api/employees/{manager.Id}/audit-events"
        );
        Assert.DoesNotContain(auditEvents!, item => item.ActionType == "RoleChanged");
    }

    [Fact]
    public async Task PickerReceivesForbiddenFromEveryEmployeeEndpoint()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = await LoginAsAsync(factory, EmployeeRole.Picker);
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Post, "/api/employees")
            {
                Content = JsonContent.Create(
                    new
                    {
                        username = "new",
                        displayName = "New",
                        initialPin = "1234",
                        role = "Picker",
                    }
                ),
            },
            new HttpRequestMessage(HttpMethod.Get, "/api/employees"),
            new HttpRequestMessage(HttpMethod.Post, "/api/employees/1/deactivate"),
            new HttpRequestMessage(HttpMethod.Post, "/api/employees/1/reactivate"),
            new HttpRequestMessage(HttpMethod.Post, "/api/employees/1/reset-pin")
            {
                Content = JsonContent.Create(new { newPin = "1234" }),
            },
            new HttpRequestMessage(HttpMethod.Post, "/api/employees/1/unlock"),
            new HttpRequestMessage(HttpMethod.Get, "/api/employees/1/audit-events"),
            new HttpRequestMessage(HttpMethod.Post, "/api/employees/1/change-role")
            {
                Content = JsonContent.Create(new { role = "Picker" }),
            },
        };

        foreach (var request in requests)
        {
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    private static async Task<HttpClient> LoginAsAsync(
        AuthWebApplicationFactory factory,
        EmployeeRole role
    )
    {
        var username = role == EmployeeRole.ManagerAdmin ? "manager" : "picker";
        await SeedEmployeeAsync(factory, username, "1234", role);
        var client = factory.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(username, "1234")
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
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

    private static async Task SetLockoutAsync(AuthWebApplicationFactory factory, int employeeId)
    {
        await factory.SeedAsync(async context =>
        {
            var employee = await context.Employees.SingleAsync(item => item.Id == employeeId);
            employee.IsLocked = true;
            employee.FailedAttemptCount = 5;
        });
    }

    private static async Task AssertEmployeeStateAsync(
        AuthWebApplicationFactory factory,
        int employeeId,
        bool isActive,
        bool isLocked,
        int failedCount
    )
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        var employee = await context
            .Employees.AsNoTracking()
            .SingleAsync(item => item.Id == employeeId);
        Assert.Equal(isActive, employee.IsActive);
        Assert.Equal(isLocked, employee.IsLocked);
        Assert.Equal(failedCount, employee.FailedAttemptCount);
    }

    private static async Task AssertEmployeeRoleAsync(
        AuthWebApplicationFactory factory,
        int employeeId,
        EmployeeRole role
    )
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        var employee = await context
            .Employees.AsNoTracking()
            .SingleAsync(item => item.Id == employeeId);
        Assert.Equal(role, employee.Role);
    }

    private sealed record CreatedEmployeeResponse(int EmployeeId);

    private sealed record ChangeRoleResponse(int EmployeeId, string Role);

    private sealed record EmployeeListResponse(
        int EmployeeId,
        string Username,
        string DisplayName,
        string Role,
        bool IsActive,
        bool IsLocked
    );

    private sealed record AuditEventResponse(
        string ActionType,
        int ActorEmployeeId,
        int? TargetEmployeeId,
        DateTimeOffset OccurredAt
    );
}
