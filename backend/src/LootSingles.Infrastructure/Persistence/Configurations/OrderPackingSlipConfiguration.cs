using LootSingles.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LootSingles.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="OrderPackingSlip"/>
/// (017-pick-completion-handoff FR-019).
/// </summary>
public class OrderPackingSlipConfiguration : IEntityTypeConfiguration<OrderPackingSlip>
{
    public void Configure(EntityTypeBuilder<OrderPackingSlip> builder)
    {
        // The order id is the key, which is what makes "at most one slip per order" a schema
        // guarantee rather than something application code has to maintain.
        builder.HasKey(slip => slip.OrderId);

        builder
            .HasOne(slip => slip.Order)
            .WithOne(order => order.PackingSlip)
            .HasForeignKey<OrderPackingSlip>(slip => slip.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(slip => slip.Content).IsRequired();
    }
}
