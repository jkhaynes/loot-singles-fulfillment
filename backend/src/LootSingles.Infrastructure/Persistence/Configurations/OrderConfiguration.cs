using LootSingles.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LootSingles.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="Order"/>.
/// Enforces a database-level uniqueness guarantee on the TCGplayer order identifier (FR-008).
/// </summary>
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasIndex(order => order.TcgplayerOrderId).IsUnique();

        // Enforces at most one active claim per employee (013-order-claiming FR-009,
        // research.md §3) as a database-level backstop alongside the conditional
        // ExecuteUpdateAsync compare-and-swap that primarily guards each claim attempt.
        builder
            .HasIndex(order => order.ClaimedByEmployeeId)
            .IsUnique()
            .HasFilter("[ClaimedByEmployeeId] IS NOT NULL");
    }
}
