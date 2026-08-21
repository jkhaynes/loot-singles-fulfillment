using LootSingles.Domain.Employees;

namespace LootSingles.Application.Auth;

/// <summary>
/// Verifies employee login credentials (FR-001, FR-004, FR-005) and records successful logins for
/// auditability (FR-018).
/// </summary>
public sealed class AuthenticationService(IEmployeeRepository repository, IPinHasher pinHasher)
{
    public async Task<AuthenticationResult> LoginAsync(
        string username, string pin, CancellationToken cancellationToken)
    {
        var employee = await repository.GetByNormalizedUsernameAsync(
            username.ToUpperInvariant(), cancellationToken);

        // A deactivated account is rejected identically to a wrong PIN (FR-005) — never reveal
        // that the username exists by checking the PIN first.
        if (employee is null || !employee.IsActive || !pinHasher.Verify(employee.PinHash, pin))
        {
            return AuthenticationResult.InvalidCredentials;
        }

        await repository.AddAuditEventAsync(
            new EmployeeAuditEvent
            {
                ActorEmployeeId = employee.Id,
                ActionType = EmployeeAuditActionType.Login,
                OccurredAt = DateTimeOffset.UtcNow,
            },
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return AuthenticationResult.Success(employee);
    }
}
