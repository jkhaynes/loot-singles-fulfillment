using LootSingles.Domain.Employees;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LootSingles.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="EmployeeAuditEvent"/> (FR-018).
/// </summary>
public class EmployeeAuditEventConfiguration : IEntityTypeConfiguration<EmployeeAuditEvent>
{
    public void Configure(EntityTypeBuilder<EmployeeAuditEvent> builder)
    {
        builder.Property(auditEvent => auditEvent.ActionType)
            .HasConversion<string>();

        builder.HasIndex(auditEvent => auditEvent.ActorEmployeeId);
        builder.HasIndex(auditEvent => auditEvent.TargetEmployeeId);
    }
}
