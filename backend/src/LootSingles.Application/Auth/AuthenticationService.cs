using LootSingles.Domain.Employees;

namespace LootSingles.Application.Auth;

/// <summary>
/// Verifies employee login credentials and records successful logins for auditability.
/// </summary>
public sealed class AuthenticationService(
    IEmployeeRepository repository,
    IPinHasher pinHasher,
    LockoutOptions lockoutOptions)
{
    public async Task<AuthenticationResult> LoginAsync(
        string username, string pin, CancellationToken cancellationToken)
    {
        var employee = await repository.GetByNormalizedUsernameAsync(
            username.ToUpperInvariant(), cancellationToken);

        // Inactive accounts always use the generic response, even if the retained record is locked.
        if (employee is null || !employee.IsActive)
        {
            return AuthenticationResult.InvalidCredentials;
        }

        if (employee.IsLocked)
        {
            return AuthenticationResult.AccountLocked;
        }

        if (!pinHasher.Verify(employee.PinHash, pin))
        {
            employee.FailedAttemptCount++;
            if (employee.FailedAttemptCount >= lockoutOptions.FailedAttemptThreshold)
            {
                employee.IsLocked = true;
            }

            await repository.SaveChangesAsync(cancellationToken);
            return AuthenticationResult.InvalidCredentials;
        }

        employee.FailedAttemptCount = 0;
        repository.AddAuditEvent(
            new EmployeeAuditEvent
            {
                ActorEmployeeId = employee.Id,
                ActionType = EmployeeAuditActionType.Login,
                OccurredAt = DateTimeOffset.UtcNow,
            });
        await repository.SaveChangesAsync(cancellationToken);

        return AuthenticationResult.Success(employee);
    }

    public async Task RecordLogoutAsync(int employeeId, CancellationToken cancellationToken)
    {
        repository.AddAuditEvent(
            new EmployeeAuditEvent
            {
                ActorEmployeeId = employeeId,
                ActionType = EmployeeAuditActionType.Logout,
                OccurredAt = DateTimeOffset.UtcNow,
            });
        await repository.SaveChangesAsync(cancellationToken);
    }
}
