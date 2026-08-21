using LootSingles.Application.Auth;
using LootSingles.Domain.Employees;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.Infrastructure.Persistence;

/// <summary>
/// <see cref="IEmployeeRepository"/> over <see cref="LootSinglesDbContext"/>.
/// </summary>
public class EmployeeRepository(LootSinglesDbContext context) : IEmployeeRepository
{
    public Task<Employee?> GetByNormalizedUsernameAsync(string normalizedUsername, CancellationToken cancellationToken) =>
        context.Employees.SingleOrDefaultAsync(employee => employee.NormalizedUsername == normalizedUsername, cancellationToken);

    public Task<Employee?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        context.Employees.SingleOrDefaultAsync(employee => employee.Id == id, cancellationToken);

    public Task AddAsync(Employee employee, CancellationToken cancellationToken)
    {
        context.Employees.Add(employee);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Employee>> ListAsync(CancellationToken cancellationToken) =>
        await context.Employees.AsNoTracking().OrderBy(employee => employee.Username).ToListAsync(cancellationToken);

    public Task AddAuditEventAsync(EmployeeAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        context.EmployeeAuditEvents.Add(auditEvent);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<EmployeeAuditEvent>> GetAuditEventsAsync(int employeeId, CancellationToken cancellationToken) =>
        await context.EmployeeAuditEvents
            .AsNoTracking()
            .Where(auditEvent => auditEvent.ActorEmployeeId == employeeId || auditEvent.TargetEmployeeId == employeeId)
            .OrderByDescending(auditEvent => auditEvent.OccurredAt)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
