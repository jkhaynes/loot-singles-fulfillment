using LootSingles.Application.Import;
using LootSingles.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LootSingles.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="ImportAttempt"/>.
/// Makes the required ImportAttempt -> ImportOrderResult relationship explicit rather than
/// relying on convention-based foreign key discovery.
/// </summary>
public class ImportAttemptConfiguration : IEntityTypeConfiguration<ImportAttempt>
{
    public void Configure(EntityTypeBuilder<ImportAttempt> builder)
    {
        builder
            .Property(attempt => attempt.Source)
            .HasDefaultValue(OrderImportSource.PackingSlipPdf);

        builder
            .HasMany(attempt => attempt.ImportOrderResults)
            .WithOne()
            .HasForeignKey(result => result.ImportAttemptId)
            .IsRequired();
    }
}
