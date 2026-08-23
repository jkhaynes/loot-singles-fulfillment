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
}
