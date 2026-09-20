using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LootSingles.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="PickingIssue"/> (015-pick-completion).
/// </summary>
public class PickingIssueConfiguration : IEntityTypeConfiguration<PickingIssue>
{
    public void Configure(EntityTypeBuilder<PickingIssue> builder)
    {
        builder.Property(issue => issue.IssueType).HasConversion<string>().HasMaxLength(50);
        builder.Property(issue => issue.Note).HasMaxLength(PickingIssue.NoteMaxLength);

        builder
            .HasOne<OrderLine>()
            .WithMany()
            .HasForeignKey(issue => issue.OrderLineId)
            .IsRequired();

        builder
            .HasOne(issue => issue.ReportedByEmployee)
            .WithMany()
            .HasForeignKey(issue => issue.ReportedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
