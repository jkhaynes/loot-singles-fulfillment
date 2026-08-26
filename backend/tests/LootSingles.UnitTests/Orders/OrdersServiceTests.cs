using LootSingles.Application.CardCatalog;
using LootSingles.Application.Orders;
using LootSingles.Domain.Orders;
using Microsoft.Extensions.Logging.Abstractions;

namespace LootSingles.UnitTests.Orders;

public sealed class OrdersServiceTests
{
    [Fact]
    public async Task GetAllAsync_WhenRepositoryIsEmpty_ReturnsEmptyList()
    {
        var service = new OrdersService(new FakeOrderRepository([]), NewEnrichmentService());

        var result = await service.GetAllAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsRepositoryProjectionUnmodified()
    {
        OrderListItem[] expected =
        [
            new(2, "ORDER-2", OrderStatus.Ready, DateTimeOffset.Parse("2026-08-22T12:00:00Z")),
            new(1, "ORDER-1", OrderStatus.Ready, DateTimeOffset.Parse("2026-08-21T12:00:00Z")),
        ];
        var service = new OrdersService(new FakeOrderRepository(expected), NewEnrichmentService());

        var result = await service.GetAllAsync(CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task GetAllAsync_IncludesClaimStateForClaimedAndUnclaimedOrders()
    {
        OrderListItem[] expected =
        [
            new(
                1,
                "ORDER-1",
                OrderStatus.InProgress,
                DateTimeOffset.Parse("2026-08-21T12:00:00Z"),
                ClaimedByEmployeeId: 7,
                ClaimedByEmployeeName: "Sam"
            ),
            new(2, "ORDER-2", OrderStatus.Ready, DateTimeOffset.Parse("2026-08-22T12:00:00Z")),
        ];
        var service = new OrdersService(new FakeOrderRepository(expected), NewEnrichmentService());

        var result = await service.GetAllAsync(CancellationToken.None);

        Assert.Equal(7, result[0].ClaimedByEmployeeId);
        Assert.Equal("Sam", result[0].ClaimedByEmployeeName);
        Assert.Null(result[1].ClaimedByEmployeeId);
        Assert.Null(result[1].ClaimedByEmployeeName);
    }

    [Fact]
    public async Task GetByIdAsync_ClaimedOrder_PreservesClaimStateFromRepository()
    {
        var claimedOrder = new OrderDetail(
            1,
            "ORDER-1",
            OrderStatus.InProgress,
            [],
            ClaimedByEmployeeId: 7,
            ClaimedByEmployeeName: "Sam"
        );
        var service = new OrdersService(
            new FakeOrderDetailRepository(claimedOrder),
            NewEnrichmentService()
        );

        var result = await service.GetByIdAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(7, result!.ClaimedByEmployeeId);
        Assert.Equal("Sam", result.ClaimedByEmployeeName);
    }

    [Fact]
    public async Task GetByIdAsync_UnclaimedOrder_HasNullClaimFields()
    {
        var unclaimedOrder = new OrderDetail(1, "ORDER-1", OrderStatus.Ready, []);
        var service = new OrdersService(
            new FakeOrderDetailRepository(unclaimedOrder),
            NewEnrichmentService()
        );

        var result = await service.GetByIdAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result!.ClaimedByEmployeeId);
        Assert.Null(result.ClaimedByEmployeeName);
    }

    [Fact]
    public async Task GetByIdAsync_GroupsLinesByProductLineAndPreservesOriginalLineOrder()
    {
        var pokemonProvider = new RecordingProvider("Pokemon");
        var magicProvider = new RecordingProvider("Magic");
        var lines = new[]
        {
            NewLine("Magic", "Lightning Bolt"),
            NewLine("Pokemon", "Pikachu"),
            NewLine("Magic", "Counterspell"),
            NewLine("Pokemon", "Charizard"),
        };
        var order = new OrderDetail(1, "ORDER-1", OrderStatus.Ready, lines);
        var enrichmentService = new CardImageEnrichmentService(
            [pokemonProvider, magicProvider],
            NullLogger<CardImageEnrichmentService>.Instance
        );
        var service = new OrdersService(new FakeOrderDetailRepository(order), enrichmentService);

        var result = await service.GetByIdAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(
            ["Lightning Bolt", "Pikachu", "Counterspell", "Charizard"],
            result!.Lines.Select(line => line.ProductName).ToArray()
        );
        Assert.Equal(
            new string?[]
            {
                "image-for-Lightning Bolt",
                "image-for-Pikachu",
                "image-for-Counterspell",
                "image-for-Charizard",
            },
            result.Lines.Select(line => line.ImageUrl).ToArray()
        );
        Assert.Equal(1, pokemonProvider.BatchCallCount);
        Assert.Equal(1, magicProvider.BatchCallCount);
        Assert.Equal(2, pokemonProvider.LastBatchSize);
        Assert.Equal(2, magicProvider.LastBatchSize);
    }

    private static OrderLineDetail NewLine(string productLine, string productName) =>
        new(productName, productLine, "Set", "#1", null, null, "Near Mint", 1);

    private static CardImageEnrichmentService NewEnrichmentService() =>
        new([], NullLogger<CardImageEnrichmentService>.Instance);

    private sealed class RecordingProvider(string productLine) : ICardCatalogProvider
    {
        public string ProductLine { get; } = productLine;
        public int BatchCallCount { get; private set; }
        public int LastBatchSize { get; private set; }

        public Task<string?> TryMatchImageUrlAsync(
            CardIdentity identity,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("Only the batch operation is expected to be called.");

        public Task<IReadOnlyDictionary<CardIdentity, string?>> TryMatchImageUrlsAsync(
            IReadOnlyList<CardIdentity> identities,
            CancellationToken cancellationToken
        )
        {
            BatchCallCount++;
            LastBatchSize = identities.Count;
            IReadOnlyDictionary<CardIdentity, string?> result = identities
                .Distinct()
                .ToDictionary(
                    identity => identity,
                    identity => (string?)$"image-for-{identity.ProductName}"
                );
            return Task.FromResult(result);
        }
    }

    private sealed class FakeOrderDetailRepository(OrderDetail order) : IOrderRepository
    {
        public Task<IReadOnlyList<OrderListItem>> GetAllAsync(
            CancellationToken cancellationToken
        ) => Task.FromResult<IReadOnlyList<OrderListItem>>([]);

        public Task<OrderDetail?> GetByIdAsync(int orderId, CancellationToken cancellationToken) =>
            Task.FromResult<OrderDetail?>(order);

        public Task<LootSingles.Domain.Orders.Order?> ClaimNextAvailableAsync(
            int actorEmployeeId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("Not exercised by these tests.");

        public Task<int?> GetActiveClaimedOrderIdAsync(
            int employeeId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("Not exercised by these tests.");

        public Task<ClaimAttemptResult> ClaimSpecificAsync(
            int orderId,
            int actorEmployeeId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("Not exercised by these tests.");
    }

    private sealed class FakeOrderRepository(IReadOnlyList<OrderListItem> orders) : IOrderRepository
    {
        public Task<IReadOnlyList<OrderListItem>> GetAllAsync(
            CancellationToken cancellationToken
        ) => Task.FromResult(orders);

        public Task<OrderDetail?> GetByIdAsync(int orderId, CancellationToken cancellationToken) =>
            Task.FromResult<OrderDetail?>(null);

        public Task<LootSingles.Domain.Orders.Order?> ClaimNextAvailableAsync(
            int actorEmployeeId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("Not exercised by these tests.");

        public Task<int?> GetActiveClaimedOrderIdAsync(
            int employeeId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("Not exercised by these tests.");

        public Task<ClaimAttemptResult> ClaimSpecificAsync(
            int orderId,
            int actorEmployeeId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("Not exercised by these tests.");
    }
}
