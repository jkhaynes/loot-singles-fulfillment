using System.Security.Claims;
using LootSingles.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LootSingles.Infrastructure.Auth;

/// <summary>
/// Rejects an otherwise-valid session cookie the moment the owning employee's account is no
/// longer active, or its role no longer matches the cookie's role claim, so deactivation
/// (FR-012, research.md §3) and role changes (014-manager-admin-screen FR-010, research.md §2)
/// both take effect on the very next request rather than waiting for the cookie's own expiry.
/// Runs on every authenticated request — the cookie authentication handler raises
/// <see cref="ValidatePrincipal"/> on each one by default.
/// </summary>
public class EmployeeSessionCookieEvents : CookieAuthenticationEvents
{
    // This is a JSON API (contracts/auth-api.md), not a page app — unauthenticated/forbidden
    // requests should get 401/403, not the cookie handler's default HTML-app redirect.
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(
        RedirectContext<CookieAuthenticationOptions> context
    )
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var employeeIdClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (employeeIdClaim is null || !int.TryParse(employeeIdClaim, out var employeeId))
        {
            context.RejectPrincipal();
            return;
        }

        var repository =
            context.HttpContext.RequestServices.GetRequiredService<IEmployeeRepository>();
        var employee = await repository.GetByIdAsync(
            employeeId,
            context.HttpContext.RequestAborted
        );

        if (employee is null || !employee.IsActive)
        {
            context.RejectPrincipal();
            return;
        }

        var roleClaim = context.Principal?.FindFirstValue(ClaimTypes.Role);
        if (roleClaim != employee.Role.ToString())
        {
            context.RejectPrincipal();
        }
    }
}
