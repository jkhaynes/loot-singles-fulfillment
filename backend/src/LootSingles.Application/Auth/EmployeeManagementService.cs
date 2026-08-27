using LootSingles.Application.Persistence;
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
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(username) || !PinFormat.IsValid(initialPin))
        {
            return EmployeeManagementResult.InvalidRequest;
        }

        var normalizedUsername = username.ToUpperInvariant();
        if (
            await repository.GetByNormalizedUsernameAsync(normalizedUsername, cancellationToken)
            is not null
        )
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

        try
        {
            // Persist first so database-generated employee IDs are available to the audit event.
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // Another request created the same case-insensitive username between the check above
            // and this save; the unique index (FR-014) is the actual source of truth.
            return EmployeeManagementResult.UsernameTaken;
        }

        await AddAuditEventAsync(
            actorEmployeeId,
            employee.Id,
            EmployeeAuditActionType.AccountCreated,
            cancellationToken
        );

        return EmployeeManagementResult.Success(employee);
    }

    public Task<EmployeeManagementResult> DeactivateAsync(
        int actorEmployeeId,
        int targetEmployeeId,
        CancellationToken cancellationToken
    ) =>
        // Only an active Manager/Admin can threaten the "at least one active Manager/Admin"
        // invariant; guarding a Picker's deactivation would incorrectly block it whenever the
        // store happens to have zero Manager/Admin employees for unrelated reasons (the guard's
        // count excludes this employee unconditionally — it has no way to know their current
        // role on its own, since it must query the database rather than this pending change).
        ApplyGuardingLastManagerAdminAsync(
            actorEmployeeId,
            targetEmployeeId,
            EmployeeAuditActionType.Deactivated,
            requiresGuard: employee => employee.Role == EmployeeRole.ManagerAdmin,
            applyAndGetRevert: employee =>
            {
                employee.IsActive = false;
                return reverted => reverted.IsActive = true;
            },
            cancellationToken
        );

    public Task<EmployeeManagementResult> ChangeRoleAsync(
        int actorEmployeeId,
        int targetEmployeeId,
        EmployeeRole newRole,
        CancellationToken cancellationToken
    ) =>
        // A no-op (setting the role an employee already holds) can never reduce the active
        // Manager/Admin count, so it never needs the guard — matching contracts/
        // employee-management-api.md's idempotent-success behavior for that case.
        ApplyGuardingLastManagerAdminAsync(
            actorEmployeeId,
            targetEmployeeId,
            EmployeeAuditActionType.RoleChanged,
            requiresGuard: employee =>
                newRole != employee.Role && employee.Role == EmployeeRole.ManagerAdmin,
            applyAndGetRevert: employee =>
            {
                var originalRole = employee.Role;
                employee.Role = newRole;
                return reverted => reverted.Role = originalRole;
            },
            cancellationToken
        );

    /// <summary>
    /// Loads the target employee, applies a mutation, records its audit event, and saves — through
    /// the last-Manager/Admin guard (research.md §1) when <paramref name="requiresGuard"/> says the
    /// mutation could threaten that invariant, or a plain save otherwise. Shared by
    /// <see cref="DeactivateAsync"/> and <see cref="ChangeRoleAsync"/>, the two mutations that can
    /// reduce the active Manager/Admin count.
    ///
    /// <paramref name="requiresGuard"/> is evaluated against the employee's state as loaded, before
    /// <paramref name="applyAndGetRevert"/> mutates it. <paramref name="applyAndGetRevert"/> applies
    /// the mutation and returns the action that undoes it — used only if the guard rejects the
    /// change, so the mutation and the audit event above are saved together with the guard's count
    /// check, inside its single held lock/transaction. A separate "check, then save" pair here would
    /// reopen the exact TOCTOU race feature 013's code-design-review already found and fixed once
    /// this session.
    /// </summary>
    private async Task<EmployeeManagementResult> ApplyGuardingLastManagerAdminAsync(
        int actorEmployeeId,
        int targetEmployeeId,
        EmployeeAuditActionType actionType,
        Func<Employee, bool> requiresGuard,
        Func<Employee, Action<Employee>> applyAndGetRevert,
        CancellationToken cancellationToken
    )
    {
        var employee = await repository.GetByIdAsync(targetEmployeeId, cancellationToken);
        if (employee is null)
        {
            return EmployeeManagementResult.NotFound;
        }

        var guardNeeded = requiresGuard(employee);
        var revert = applyAndGetRevert(employee);
        repository.AddAuditEvent(
            new EmployeeAuditEvent
            {
                ActorEmployeeId = actorEmployeeId,
                TargetEmployeeId = targetEmployeeId,
                ActionType = actionType,
                OccurredAt = DateTimeOffset.UtcNow,
            }
        );

        if (!guardNeeded)
        {
            await repository.SaveChangesAsync(cancellationToken);
            return EmployeeManagementResult.Success(employee);
        }

        if (
            !await repository.SaveChangesGuardingLastManagerAdminAsync(
                targetEmployeeId,
                cancellationToken
            )
        )
        {
            revert(employee);
            return EmployeeManagementResult.WouldRemoveLastManagerAdmin;
        }

        return EmployeeManagementResult.Success(employee);
    }

    public Task<EmployeeManagementResult> ReactivateAsync(
        int actorEmployeeId,
        int targetEmployeeId,
        CancellationToken cancellationToken
    ) =>
        ChangeAsync(
            actorEmployeeId,
            targetEmployeeId,
            EmployeeAuditActionType.Reactivated,
            employee =>
            {
                employee.IsActive = true;
                employee.IsLocked = false;
                employee.FailedAttemptCount = 0;
            },
            cancellationToken
        );

    public Task<EmployeeManagementResult> ResetPinAsync(
        int actorEmployeeId,
        int targetEmployeeId,
        string newPin,
        CancellationToken cancellationToken
    )
    {
        if (!PinFormat.IsValid(newPin))
        {
            return Task.FromResult(EmployeeManagementResult.InvalidRequest);
        }

        return ChangeAsync(
            actorEmployeeId,
            targetEmployeeId,
            EmployeeAuditActionType.PinReset,
            employee => employee.PinHash = pinHasher.Hash(newPin),
            cancellationToken
        );
    }

    public Task<EmployeeManagementResult> UnlockAsync(
        int actorEmployeeId,
        int targetEmployeeId,
        CancellationToken cancellationToken
    ) =>
        ChangeAsync(
            actorEmployeeId,
            targetEmployeeId,
            EmployeeAuditActionType.Unlocked,
            employee =>
            {
                employee.IsLocked = false;
                employee.FailedAttemptCount = 0;
            },
            cancellationToken
        );

    public Task<IReadOnlyList<Employee>> ListAsync(CancellationToken cancellationToken) =>
        repository.ListAsync(cancellationToken);

    public Task<Employee?> GetByIdAsync(int employeeId, CancellationToken cancellationToken) =>
        repository.GetByIdAsync(employeeId, cancellationToken);

    public Task<bool> ExistsAsync(int employeeId, CancellationToken cancellationToken) =>
        repository.ExistsAsync(employeeId, cancellationToken);

    public Task<IReadOnlyList<EmployeeAuditEvent>> GetAuditEventsAsync(
        int employeeId,
        CancellationToken cancellationToken
    ) => repository.GetAuditEventsAsync(employeeId, cancellationToken);

    private async Task<EmployeeManagementResult> ChangeAsync(
        int actorEmployeeId,
        int targetEmployeeId,
        EmployeeAuditActionType actionType,
        Action<Employee> apply,
        CancellationToken cancellationToken
    )
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
        CancellationToken cancellationToken
    )
    {
        repository.AddAuditEvent(
            new EmployeeAuditEvent
            {
                ActorEmployeeId = actorEmployeeId,
                TargetEmployeeId = targetEmployeeId,
                ActionType = actionType,
                OccurredAt = DateTimeOffset.UtcNow,
            }
        );
        await repository.SaveChangesAsync(cancellationToken);
    }
}
