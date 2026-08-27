using LootSingles.Application.Auth;
using LootSingles.Application.Persistence;
using LootSingles.Domain.Employees;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.Infrastructure.Persistence;

/// <summary>
/// <see cref="IEmployeeRepository"/> over <see cref="LootSinglesDbContext"/>.
/// </summary>
public class EmployeeRepository(LootSinglesDbContext context) : IEmployeeRepository
{
    /// <summary>
    /// Resource name for the SQL Server application lock (<c>sp_getapplock</c>) that serializes
    /// first-employee bootstrap across processes. Held only for the duration of the owning
    /// transaction, so it is released automatically on commit or rollback.
    /// </summary>
    private const string BootstrapLockResource = "LootSingles.Employees.Bootstrap";

    /// <summary>
    /// Resource name for the SQL Server application lock guarding the "at least one active
    /// Manager/Admin must remain" invariant (014-manager-admin-screen FR-007, research.md §1) —
    /// the same technique as <see cref="BootstrapLockResource"/>, applied to a different invariant.
    /// </summary>
    private const string LastManagerAdminGuardLockResource =
        "LootSingles.Employees.LastManagerAdminGuard";

    public async Task<bool> TryAddFirstEmployeeAsync(
        Employee employee,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken
        );

        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"
DECLARE @lockResult int;
EXEC @lockResult = sp_getapplock
    @Resource = {BootstrapLockResource},
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 30000;
IF @lockResult < 0
BEGIN
    THROW 51000, 'Could not acquire the exclusive employee-bootstrap lock.', 1;
END",
            cancellationToken
        );

        if (await context.Employees.AsNoTracking().AnyAsync(cancellationToken))
        {
            return false;
        }

        context.Employees.Add(employee);
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task<Employee?> GetByNormalizedUsernameAsync(
        string normalizedUsername,
        CancellationToken cancellationToken
    ) =>
        context.Employees.SingleOrDefaultAsync(
            employee => employee.NormalizedUsername == normalizedUsername,
            cancellationToken
        );

    public Task<Employee?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        context.Employees.SingleOrDefaultAsync(employee => employee.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken) =>
        context.Employees.AnyAsync(employee => employee.Id == id, cancellationToken);

    public void Add(Employee employee) => context.Employees.Add(employee);

    public async Task<IReadOnlyList<Employee>> ListAsync(CancellationToken cancellationToken) =>
        await context
            .Employees.AsNoTracking()
            .OrderBy(employee => employee.Username)
            .ToListAsync(cancellationToken);

    public void AddAuditEvent(EmployeeAuditEvent auditEvent) =>
        context.EmployeeAuditEvents.Add(auditEvent);

    public async Task<IReadOnlyList<EmployeeAuditEvent>> GetAuditEventsAsync(
        int employeeId,
        CancellationToken cancellationToken
    )
    {
        return await context
            .EmployeeAuditEvents.AsNoTracking()
            .Where(auditEvent =>
                auditEvent.ActorEmployeeId == employeeId
                || auditEvent.TargetEmployeeId == employeeId
            )
            .OrderByDescending(auditEvent => auditEvent.OccurredAt)
            .ThenByDescending(auditEvent => auditEvent.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (DuplicateKeyDetector.IsDuplicateKeyViolation(exception))
        {
            throw new UniqueConstraintViolationException("Username is already in use.", exception);
        }
    }

    public async Task<bool> SaveChangesGuardingLastManagerAdminAsync(
        int excludingEmployeeId,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken
        );

        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"
DECLARE @lockResult int;
EXEC @lockResult = sp_getapplock
    @Resource = {LastManagerAdminGuardLockResource},
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 30000;
IF @lockResult < 0
BEGIN
    THROW 51000, 'Could not acquire the exclusive last-Manager/Admin guard lock.', 1;
END",
            cancellationToken
        );

        var remainingActiveManagerAdmins = await context
            .Employees.AsNoTracking()
            .CountAsync(
                employee =>
                    employee.Id != excludingEmployeeId
                    && employee.IsActive
                    && employee.Role == EmployeeRole.ManagerAdmin,
                cancellationToken
            );

        if (remainingActiveManagerAdmins < 1)
        {
            return false;
        }

        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
