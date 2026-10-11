using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Import;

public class DiscardOrderTests
{
    [Fact]
    public async Task DiscardOrder_OrderWithPackingSlip_LeavesNothingTrackedAndLaterSaveInsertsNoSlip()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext();
        var repository = new ImportRepository(context);
        var order = new Order
        {
            TcgplayerOrderId = "DISCARD-SLIP-0001",
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow,
            OrderLines =
            [
                new OrderLine
                {
                    RawDescription = "raw",
                    ProductLine = "Magic",
                    ProductName = "Card",
                    Set = "Set",
                    CollectorNumber = "1",
                    Condition = "Near Mint",
                    Quantity = 1,
                },
            ],
            PackingSlip = new OrderPackingSlip
            {
                Content = [1, 2, 3],
                StoredAt = DateTimeOffset.UtcNow,
            },
        };
        repository.AddOrder(order);
        Assert.Single(context.ChangeTracker.Entries<OrderPackingSlip>());

        repository.DiscardOrder(order);

        Assert.Empty(context.ChangeTracker.Entries<OrderPackingSlip>());
        // A leaked slip would make the next save (the per-order save in the import loop)
        // try to insert it against an order that was never stored.
        await repository.SaveChangesAsync(CancellationToken.None);
        Assert.Empty(await context.Orders.AsNoTracking().ToListAsync());
        Assert.Empty(await context.Set<OrderPackingSlip>().AsNoTracking().ToListAsync());
    }
}
