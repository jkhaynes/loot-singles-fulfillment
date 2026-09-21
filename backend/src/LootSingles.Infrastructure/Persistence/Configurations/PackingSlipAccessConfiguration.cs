using LootSingles.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LootSingles.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="PackingSlipAccess"/>
/// (017-pick-completion-handoff FR-038).
/// </summary>
public class PackingSlipAccessConfiguration : IEntityTypeConfiguration<PackingSlipAccess>
{
    public void Configure(EntityTypeBuilder<PackingSlipAccess> builder)
    {
        builder
            .HasOne(access => access.Order)
            .WithMany()
            .HasForeignKey(access => access.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // An access row outlives the employee record's usefulness: deleting an employee must not
        // erase the evidence of who opened a customer's slip, which is the whole point of the row.
        builder
            .HasOne(access => access.Employee)
            .WithMany()
            .HasForeignKey(access => access.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        // The question this table answers is "who opened this order's slip", so that is the
        // access path it is indexed for.
        builder.HasIndex(access => new { access.OrderId, access.RetrievedAt });
    }
}
