using System.Security.Claims;
using LootSingles.Application.Auth;
using LootSingles.Domain.Employees;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LootSingles.Api.Controllers;

/// <summary>
/// Manager/Admin-only employee roster maintenance API (contracts/employee-management-api.md).
/// </summary>
[ApiController]
[Route("api/employees")]
[Authorize(Roles = nameof(EmployeeRole.ManagerAdmin))]
public sealed class EmployeesController(EmployeeManagementService managementService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateEmployeeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username)
            || string.IsNullOrWhiteSpace(request.DisplayName)
            || !IsValidPin(request.InitialPin)
            || !Enum.TryParse<EmployeeRole>(request.Role, ignoreCase: false, out var role))
        {
            return BadRequest(new ErrorResponse("invalid_request", "Valid employee details are required."));
        }

        var result = await managementService.CreateAsync(
            ActorEmployeeId(),
            request.Username,
            request.DisplayName,
            request.InitialPin!,
            role,
            cancellationToken);

        if (result.Outcome == EmployeeManagementOutcome.UsernameTaken)
        {
            return Conflict(new ErrorResponse("username_taken", "Username is already in use."));
        }

        return StatusCode(StatusCodes.Status201Created, new CreatedEmployeeResponse(result.Employee!.Id));
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var employees = await managementService.ListAsync(cancellationToken);
        return Ok(employees.Select(employee => new EmployeeListResponse(
            employee.Id,
            employee.Username,
            employee.DisplayName,
            employee.Role.ToString(),
            employee.IsActive,
            employee.IsLocked)));
    }

    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        var result = await managementService.DeactivateAsync(ActorEmployeeId(), id, cancellationToken);
        return result.Outcome == EmployeeManagementOutcome.NotFound ? NotFound() : NoContent();
    }

    [HttpPost("{id:int}/reactivate")]
    public async Task<IActionResult> Reactivate(int id, CancellationToken cancellationToken)
    {
        var result = await managementService.ReactivateAsync(ActorEmployeeId(), id, cancellationToken);
        return result.Outcome == EmployeeManagementOutcome.NotFound ? NotFound() : NoContent();
    }

    [HttpPost("{id:int}/reset-pin")]
    public async Task<IActionResult> ResetPin(
        int id, [FromBody] ResetPinRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidPin(request.NewPin))
        {
            return BadRequest(new ErrorResponse("invalid_request", "A 4-digit numeric PIN is required."));
        }

        var result = await managementService.ResetPinAsync(
            ActorEmployeeId(), id, request.NewPin!, cancellationToken);
        return result.Outcome == EmployeeManagementOutcome.NotFound ? NotFound() : Ok();
    }

    [HttpPost("{id:int}/unlock")]
    public async Task<IActionResult> Unlock(int id, CancellationToken cancellationToken)
    {
        var result = await managementService.UnlockAsync(ActorEmployeeId(), id, cancellationToken);
        return result.Outcome == EmployeeManagementOutcome.NotFound ? NotFound() : NoContent();
    }

    [HttpGet("{id:int}/audit-events")]
    public async Task<IActionResult> AuditEvents(int id, CancellationToken cancellationToken)
    {
        if (await managementService.GetByIdAsync(id, cancellationToken) is null)
        {
            return NotFound();
        }

        var auditEvents = await managementService.GetAuditEventsAsync(id, cancellationToken);
        return Ok(auditEvents.Select(auditEvent => new EmployeeAuditEventResponse(
            auditEvent.ActionType.ToString(),
            auditEvent.ActorEmployeeId,
            auditEvent.TargetEmployeeId,
            auditEvent.OccurredAt)));
    }

    private int ActorEmployeeId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static bool IsValidPin(string? pin) =>
        pin is { Length: 4 } && pin.All(char.IsAsciiDigit);
}

public sealed record CreateEmployeeRequest(
    string? Username, string? DisplayName, string? InitialPin, string? Role);

public sealed record ResetPinRequest(string? NewPin);

public sealed record CreatedEmployeeResponse(int EmployeeId);

public sealed record EmployeeListResponse(
    int EmployeeId, string Username, string DisplayName, string Role, bool IsActive, bool IsLocked);

public sealed record EmployeeAuditEventResponse(
    string ActionType, int ActorEmployeeId, int? TargetEmployeeId, DateTimeOffset OccurredAt);
