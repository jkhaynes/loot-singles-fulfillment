using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Persistence;

public class OrderConfigurationTests
{
    // This metadata assertion is database-free; real SQL Server enforcement is covered by
    // SqlServerSchemaTests.
    [Fact]
    public void Model_ForOrder_HasUniqueIndexOnTcgplayerOrderId()
    {
        var options = new DbContextOptionsBuilder<LootSinglesDbContext>()
            .UseSqlServer(
                "Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true"
            )
            .Options;

        using var context = new LootSinglesDbContext(options);

        var entityType = context.Model.FindEntityType(typeof(Order));
        Assert.NotNull(entityType);

        var index = entityType
            .GetIndexes()
            .SingleOrDefault(i =>
                i.Properties.Select(p => p.Name).SequenceEqual([nameof(Order.TcgplayerOrderId)])
            );

        Assert.NotNull(index);
        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Model_ForOrder_HasUniqueIndexOnClaimedByEmployeeId()
    {
        var options = new DbContextOptionsBuilder<LootSinglesDbContext>()
            .UseSqlServer(
                "Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true"
            )
            .Options;

        using var context = new LootSinglesDbContext(options);

        var entityType = context.Model.FindEntityType(typeof(Order));
        Assert.NotNull(entityType);

        var index = entityType
            .GetIndexes()
            .SingleOrDefault(i =>
                i.Properties.Select(p => p.Name).SequenceEqual([nameof(Order.ClaimedByEmployeeId)])
            );

        Assert.NotNull(index);
        Assert.True(index.IsUnique);
    }
}
