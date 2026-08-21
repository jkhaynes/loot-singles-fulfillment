using LootSingles.Domain.Employees;

namespace LootSingles.Application.Auth;

/// <summary>
/// Performs Manager/Admin employee-roster operations and records their audit trail.
/// </summary>
public sealed class EmployeeManagementService(IEmployeeRepository repository, IPinHasher pinHasher)
{
    public async Task<EmployeeManagementResult> CreateAsync(
        int actorEmployeeId,
        string username,
        string displayName,
        string initialPin,
        EmployeeRole role,
        CancellationToken cancellationToken)
    {
        var normalizedUsername = username.ToUpperInvariant();
        if (await repository.GetByNormalizedUsernameAsync(normalizedUsername, cancellationToken) is not null)
        {
            return EmployeeManagementResult.UsernameTaken;
        }

        var employee = new Employee
        {
            Username = username,
            NormalizedUsername = normalizedUsername,
            DisplayName = displayName,
            PinHash = pinHasher.Hash(initialPin),
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        repository.Add(employee);
        // Persist first so database-generated employee IDs are available to the audit event.
        await repository.SaveChangesAsync(cancellationToken);
        await AddAuditEventAsync(
            actorEmployeeId, employee.Id, EmployeeAuditActionType.AccountCreated, cancellationToken);

        return EmployeeManagementResult.Success(employee);
    }

    public Task<EmployeeManagementResult> DeactivateAsync(
        int actorEmployeeId, int targetEmployeeId, CancellationToken cancellationToken) =>
        ChangeAsync(actorEmployeeId, targetEmployeeId, EmployeeAuditActionType.Deactivated,
            employee => employee.IsActive = false, cancellationToken);

    public Task<EmployeeManagementResult> ReactivateAsync(
        int actorEmployeeId, int targetEmployeeId, CancellationToken cancellationToken) =>
        ChangeAsync(actorEmployeeId, targetEmployeeId, EmployeeAuditActionType.Reactivated,
            employee =>
            {
                employee.IsActive = true;
                employee.IsLocked = false;
                employee.FailedAttemptCount = 0;
            }, cancellationToken);

    public Task<EmployeeManagementResult> ResetPinAsync(
        int actorEmployeeId, int targetEmployeeId, string newPin, CancellationToken cancellationToken) =>
        ChangeAsync(actorEmployeeId, targetEmployeeId, EmployeeAuditActionType.PinReset,
            employee => employee.PinHash = pinHasher.Hash(newPin), cancellationToken);

    public Task<EmployeeManagementResult> UnlockAsync(
        int actorEmployeeId, int targetEmployeeId, CancellationToken cancellationToken) =>
        ChangeAsync(actorEmployeeId, targetEmployeeId, EmployeeAuditActionType.Unlocked,
            employee =>
            {
                employee.IsLocked = false;
                employee.FailedAttemptCount = 0;
            }, cancellationToken);

    public Task<IReadOnlyList<Employee>> ListAsync(CancellationToken cancellationToken) =>
        repository.ListAsync(cancellationToken);

    public Task<Employee?> GetByIdAsync(int employeeId, CancellationToken cancellationToken) =>
        repository.GetByIdAsync(employeeId, cancellationToken);

    public Task<IReadOnlyList<EmployeeAuditEvent>> GetAuditEventsAsync(
        int employeeId, CancellationToken cancellationToken) =>
        repository.GetAuditEventsAsync(employeeId, cancellationToken);

    private async Task<EmployeeManagementResult> ChangeAsync(
        int actorEmployeeId,
        int targetEmployeeId,
        EmployeeAuditActionType actionType,
        Action<Employee> apply,
        CancellationToken cancellationToken)
    {
        var employee = await repository.GetByIdAsync(targetEmployeeId, cancellationToken);
        if (employee is null)
        {
            return EmployeeManagementResult.NotFound;
        }

        apply(employee);
        await AddAuditEventAsync(actorEmployeeId, targetEmployeeId, actionType, cancellationToken);
        return EmployeeManagementResult.Success(employee);
    }

    private async Task AddAuditEventAsync(
        int actorEmployeeId,
        int targetEmployeeId,
        EmployeeAuditActionType actionType,
        CancellationToken cancellationToken)
    {
        repository.AddAuditEvent(
            new EmployeeAuditEvent
            {
                ActorEmployeeId = actorEmployeeId,
                TargetEmployeeId = targetEmployeeId,
                ActionType = actionType,
                OccurredAt = DateTimeOffset.UtcNow,
            });
        await repository.SaveChangesAsync(cancellationToken);
    }
}
