using LootSingles.Domain.Employees;

namespace LootSingles.Application.Auth;

/// <summary>
/// Persistence seam for <see cref="Employee"/> and <see cref="EmployeeAuditEvent"/>, keeping
/// <c>LootSingles.Application</c> and <c>LootSingles.Domain</c> free of a direct EF Core dependency
/// (Constitution IX/XII).
/// </summary>
public interface IEmployeeRepository
{
    /// <summary>
    /// Atomically inserts <paramref name="employee"/> only if the employee store is currently empty,
    /// serialized cross-process so exactly one caller can win a concurrent race. Returns
    /// <see langword="true"/> if the employee was inserted, or <see langword="false"/> if the store
    /// already contained at least one employee (no modification is made in that case).
    /// </summary>
    Task<bool> TryAddFirstEmployeeAsync(Employee employee, CancellationToken cancellationToken);

    Task<Employee?> GetByNormalizedUsernameAsync(
        string normalizedUsername,
        CancellationToken cancellationToken
    );

    Task<Employee?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken);

    void Add(Employee employee);

    Task<IReadOnlyList<Employee>> ListAsync(CancellationToken cancellationToken);

    void AddAuditEvent(EmployeeAuditEvent auditEvent);

    Task<IReadOnlyList<EmployeeAuditEvent>> GetAuditEventsAsync(
        int employeeId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Persists all pending changes (additions and modifications to tracked entities).
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists all pending changes, but only if doing so would not leave zero active
    /// Manager/Admin employees once <paramref name="excludingEmployeeId"/> is excluded from the
    /// count (014-manager-admin-screen FR-007) — that employee is excluded because the pending
    /// change being saved is what's being guarded (a deactivation or a role change away from
    /// Manager/Admin). The count and the save happen inside one serialized transaction
    /// (research.md §1), so this is race-free against a concurrent call with a different excluded
    /// employee. Returns <see langword="false"/>, without saving anything, if the guard would be
    /// violated.
    /// </summary>
    Task<bool> SaveChangesGuardingLastManagerAdminAsync(
        int excludingEmployeeId,
        CancellationToken cancellationToken
    );
}
