using LootSingles.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LootSingles.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="OrderLine"/>.
/// Makes the required Order -> OrderLine relationship explicit rather than relying on
/// convention-based foreign key discovery.
/// </summary>
public class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.HasOne<Order>()
            .WithMany(order => order.OrderLines)
            .HasForeignKey(orderLine => orderLine.OrderId)
            .IsRequired();
    }
}
