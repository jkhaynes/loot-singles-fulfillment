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
public sealed class EmployeesController(EmployeeManagementService managementService)
    : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateEmployeeRequest request,
        CancellationToken cancellationToken
    )
    {
        if (
            string.IsNullOrWhiteSpace(request.DisplayName)
            || !Enum.TryParse<EmployeeRole>(request.Role, ignoreCase: false, out var role)
        )
        {
            return BadRequest(
                new ErrorResponse("invalid_request", "Valid employee details are required.")
            );
        }

        var result = await managementService.CreateAsync(
            ActorEmployeeId(),
            request.Username ?? string.Empty,
            request.DisplayName,
            request.InitialPin ?? string.Empty,
            role,
            cancellationToken
        );

        if (result.Outcome == EmployeeManagementOutcome.InvalidRequest)
        {
            return BadRequest(
                new ErrorResponse("invalid_request", "Valid employee details are required.")
            );
        }

        if (result.Outcome == EmployeeManagementOutcome.UsernameTaken)
        {
            return Conflict(new ErrorResponse("username_taken", "Username is already in use."));
        }

        return StatusCode(
            StatusCodes.Status201Created,
            new CreatedEmployeeResponse(result.Employee!.Id)
        );
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var employees = await managementService.ListAsync(cancellationToken);
        return Ok(
            employees.Select(employee => new EmployeeListResponse(
                employee.Id,
                employee.Username,
                employee.DisplayName,
                employee.Role.ToString(),
                employee.IsActive,
                employee.IsLocked
            ))
        );
    }

    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        var result = await managementService.DeactivateAsync(
            ActorEmployeeId(),
            id,
            cancellationToken
        );

        if (result.Outcome == EmployeeManagementOutcome.NotFound)
        {
            return NotFound();
        }

        if (result.Outcome == EmployeeManagementOutcome.WouldRemoveLastManagerAdmin)
        {
            return Conflict(
                new ErrorResponse(
                    "would_remove_last_manager_admin",
                    "Deactivating this employee would leave zero active Manager/Admin employees."
                )
            );
        }

        return NoContent();
    }

    [HttpPost("{id:int}/reactivate")]
    public async Task<IActionResult> Reactivate(int id, CancellationToken cancellationToken)
    {
        var result = await managementService.ReactivateAsync(
            ActorEmployeeId(),
            id,
            cancellationToken
        );
        return result.Outcome == EmployeeManagementOutcome.NotFound ? NotFound() : NoContent();
    }

    [HttpPost("{id:int}/reset-pin")]
    public async Task<IActionResult> ResetPin(
        int id,
        [FromBody] ResetPinRequest request,
        CancellationToken cancellationToken
    )
    {
        var result = await managementService.ResetPinAsync(
            ActorEmployeeId(),
            id,
            request.NewPin ?? string.Empty,
            cancellationToken
        );

        if (result.Outcome == EmployeeManagementOutcome.InvalidRequest)
        {
            return BadRequest(
                new ErrorResponse("invalid_request", "A 4-digit numeric PIN is required.")
            );
        }

        return result.Outcome == EmployeeManagementOutcome.NotFound ? NotFound() : Ok();
    }

    [HttpPost("{id:int}/unlock")]
    public async Task<IActionResult> Unlock(int id, CancellationToken cancellationToken)
    {
        var result = await managementService.UnlockAsync(ActorEmployeeId(), id, cancellationToken);
        return result.Outcome == EmployeeManagementOutcome.NotFound ? NotFound() : NoContent();
    }

    [HttpPost("{id:int}/change-role")]
    public async Task<IActionResult> ChangeRole(
        int id,
        [FromBody] ChangeRoleRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!Enum.TryParse<EmployeeRole>(request.Role, ignoreCase: false, out var role))
        {
            return BadRequest(new ErrorResponse("invalid_request", "A valid role is required."));
        }

        var result = await managementService.ChangeRoleAsync(
            ActorEmployeeId(),
            id,
            role,
            cancellationToken
        );

        if (result.Outcome == EmployeeManagementOutcome.NotFound)
        {
            return NotFound();
        }

        if (result.Outcome == EmployeeManagementOutcome.WouldRemoveLastManagerAdmin)
        {
            return Conflict(
                new ErrorResponse(
                    "would_remove_last_manager_admin",
                    "Changing this employee's role would leave zero active Manager/Admin employees."
                )
            );
        }

        return Ok(new ChangeRoleResponse(result.Employee!.Id, result.Employee.Role.ToString()));
    }

    [HttpGet("{id:int}/audit-events")]
    public async Task<IActionResult> AuditEvents(int id, CancellationToken cancellationToken)
    {
        if (!await managementService.ExistsAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var auditEvents = await managementService.GetAuditEventsAsync(id, cancellationToken);
        return Ok(
            auditEvents.Select(auditEvent => new EmployeeAuditEventResponse(
                auditEvent.ActionType.ToString(),
                auditEvent.ActorEmployeeId,
                auditEvent.TargetEmployeeId,
                auditEvent.OccurredAt
            ))
        );
    }

    private int ActorEmployeeId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public sealed record CreateEmployeeRequest(
    string? Username,
    string? DisplayName,
    string? InitialPin,
    string? Role
);

public sealed record ResetPinRequest(string? NewPin);

public sealed record ChangeRoleRequest(string? Role);

public sealed record ChangeRoleResponse(int EmployeeId, string Role);

public sealed record CreatedEmployeeResponse(int EmployeeId);

public sealed record EmployeeListResponse(
    int EmployeeId,
    string Username,
    string DisplayName,
    string Role,
    bool IsActive,
    bool IsLocked
);

public sealed record EmployeeAuditEventResponse(
    string ActionType,
    int ActorEmployeeId,
    int? TargetEmployeeId,
    DateTimeOffset OccurredAt
);
