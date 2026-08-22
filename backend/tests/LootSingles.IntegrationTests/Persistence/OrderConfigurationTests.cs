using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Persistence;

public class OrderConfigurationTests
{
    // EF Core's InMemory provider does not enforce unique indexes (only relational providers do),
    // so this asserts against the compiled model instead of provoking a real constraint violation.
    // FR-008's actual database-enforced-duplicate-rejection behavior is verified against a real
    // relational provider by the later import-pipeline tests (tasks T033/T034).
    [Fact]
    public void Model_ForOrder_HasUniqueIndexOnTcgplayerOrderId()
    {
        var options = new DbContextOptionsBuilder<LootSinglesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
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
}
