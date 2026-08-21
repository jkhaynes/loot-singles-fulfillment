using LootSingles.Domain.Employees;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LootSingles.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="Employee"/>.
/// Enforces case-insensitive username uniqueness via a unique index on the normalized column (FR-014, research.md §7).
/// </summary>
public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.Property(employee => employee.Role)
            .HasConversion<string>();

        builder.HasIndex(employee => employee.NormalizedUsername)
            .IsUnique();
    }
}
