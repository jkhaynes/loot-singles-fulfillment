using LootSingles.Application.Import;
using LootSingles.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LootSingles.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="ImportOrderResult"/>.
/// Makes the optional ImportOrderResult -> Order relationship explicit (set only when the
/// order succeeded, per FR-007/FR-012) rather than relying on convention-based discovery.
/// </summary>
public class ImportOrderResultConfiguration : IEntityTypeConfiguration<ImportOrderResult>
{
    public void Configure(EntityTypeBuilder<ImportOrderResult> builder)
    {
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(result => result.ResultingOrderId)
            .IsRequired(false);
    }
}
