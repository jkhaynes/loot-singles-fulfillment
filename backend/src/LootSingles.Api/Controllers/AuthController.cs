using System.Security.Claims;
using System.Text.RegularExpressions;
using LootSingles.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AuthenticationService = LootSingles.Application.Auth.AuthenticationService;

namespace LootSingles.Api.Controllers;

/// <summary>
/// Login, logout, and current-session lookup (contracts/auth-api.md). All endpoints are
/// same-origin, cookie-based (research.md §2); no bearer token is ever returned to the client.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed partial class AuthController(AuthenticationService authenticationService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Username) || !FourDigitPinPattern().IsMatch(request.Pin ?? string.Empty))
        {
            return BadRequest(new ErrorResponse("invalid_request", "Username and a 4-digit numeric PIN are required."));
        }

        var result = await authenticationService.LoginAsync(request.Username, request.Pin!, cancellationToken);

        if (result.Outcome == AuthenticationOutcome.InvalidCredentials)
        {
            return Unauthorized(new ErrorResponse("invalid_credentials", "Username or PIN is incorrect."));
        }

        var employee = result.Employee!;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, employee.Id.ToString()),
                new Claim(ClaimTypes.Name, employee.DisplayName),
                new Claim(ClaimTypes.Role, employee.Role.ToString()),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return Ok(ToResponse(employee.Id, employee.DisplayName, employee.Role.ToString()));
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var employeeId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var role = User.FindFirstValue(ClaimTypes.Role)!;
        var displayName = User.Identity?.Name ?? string.Empty;

        return Ok(ToResponse(employeeId, displayName, role));
    }

    private static AuthResponse ToResponse(int employeeId, string displayName, string role) =>
        new(employeeId, displayName, role);

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex FourDigitPinPattern();
}

public sealed record LoginRequest(string? Username, string? Pin);

public sealed record AuthResponse(int EmployeeId, string DisplayName, string Role);

public sealed record ErrorResponse(string Error, string Message);
