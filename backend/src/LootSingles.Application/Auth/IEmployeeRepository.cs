using LootSingles.Domain.Employees;

namespace LootSingles.Application.Auth;

/// <summary>
/// Persistence seam for <see cref="Employee"/> and <see cref="EmployeeAuditEvent"/>, keeping
/// <c>LootSingles.Application</c> and <c>LootSingles.Domain</c> free of a direct EF Core dependency
/// (Constitution IX/XII).
/// </summary>
public interface IEmployeeRepository
{
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
}
