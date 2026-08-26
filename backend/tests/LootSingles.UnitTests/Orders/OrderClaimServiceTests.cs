using LootSingles.Application.Orders;
using LootSingles.Application.Persistence;
using LootSingles.Domain.Orders;
using Microsoft.Extensions.Logging.Abstractions;

namespace LootSingles.UnitTests.Orders;

public sealed class OrderClaimServiceTests
{
    [Fact]
    public async Task PickNextAsync_OrderAvailable_ReturnsSuccessWithClaimedOrder()
    {
        var claimedOrder = NewOrder(5, "ORDER-5");
        var repository = new FakeOrderRepository { OrderToClaim = claimedOrder };
        var service = NewService(repository);

        var result = await service.PickNextAsync(actorEmployeeId: 1, CancellationToken.None);

        Assert.Equal(OrderClaimOutcome.Success, result.Outcome);
        Assert.Same(claimedOrder, result.Order);
    }

    [Fact]
    public async Task PickNextAsync_NoOrdersAvailable_ReturnsNoOrdersAvailable()
    {
        var repository = new FakeOrderRepository { OrderToClaim = null };
        var service = NewService(repository);

        var result = await service.PickNextAsync(actorEmployeeId: 1, CancellationToken.None);

        Assert.Equal(OrderClaimOutcome.NoOrdersAvailable, result.Outcome);
        Assert.Null(result.Order);
    }

    [Fact]
    public async Task PickNextAsync_EmployeeAlreadyHasActiveClaim_ReturnsEmployeeHasActiveClaimWithoutAttemptingClaim()
    {
        var repository = new FakeOrderRepository
        {
            InitialActiveClaimedOrderId = 42,
            OrderToClaim = NewOrder(5, "ORDER-5"),
        };
        var service = NewService(repository);

        var result = await service.PickNextAsync(actorEmployeeId: 1, CancellationToken.None);

        Assert.Equal(OrderClaimOutcome.EmployeeHasActiveClaim, result.Outcome);
        Assert.Equal(42, result.ConflictingOrderId);
        Assert.False(repository.ClaimNextAvailableCalled);
    }

    [Fact]
    public async Task PickNextAsync_LosesRaceToOwnConcurrentClaim_ReturnsEmployeeHasActiveClaim()
    {
        var repository = new FakeOrderRepository
        {
            InitialActiveClaimedOrderId = null,
            ThrowUniqueViolationOnClaim = true,
            ActiveClaimedOrderIdAfterRace = 7,
        };
        var service = NewService(repository);

        var result = await service.PickNextAsync(actorEmployeeId: 1, CancellationToken.None);

        Assert.Equal(OrderClaimOutcome.EmployeeHasActiveClaim, result.Outcome);
        Assert.Equal(7, result.ConflictingOrderId);
    }

    private static OrderClaimService NewService(IOrderRepository repository) =>
        new(repository, NullLogger<OrderClaimService>.Instance);

    private static Order NewOrder(int id, string tcgplayerOrderId) =>
        new()
        {
            Id = id,
            TcgplayerOrderId = tcgplayerOrderId,
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow,
        };

    private sealed class FakeOrderRepository : IOrderRepository
    {
        public Order? OrderToClaim { get; set; }
        public int? InitialActiveClaimedOrderId { get; set; }
        public bool ThrowUniqueViolationOnClaim { get; set; }
        public int? ActiveClaimedOrderIdAfterRace { get; set; }
        public bool ClaimNextAvailableCalled { get; private set; }

        public Task<IReadOnlyList<OrderListItem>> GetAllAsync(
            CancellationToken cancellationToken
        ) => Task.FromResult<IReadOnlyList<OrderListItem>>([]);

        public Task<OrderDetail?> GetByIdAsync(int orderId, CancellationToken cancellationToken) =>
            Task.FromResult<OrderDetail?>(null);

        public Task<Order?> ClaimNextAvailableAsync(
            int actorEmployeeId,
            CancellationToken cancellationToken
        )
        {
            ClaimNextAvailableCalled = true;
            if (ThrowUniqueViolationOnClaim)
            {
                throw new UniqueConstraintViolationException(
                    "Simulated race.",
                    new InvalidOperationException()
                );
            }

            return Task.FromResult(OrderToClaim);
        }

        public Task<int?> GetActiveClaimedOrderIdAsync(
            int employeeId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                ClaimNextAvailableCalled
                    ? ActiveClaimedOrderIdAfterRace
                    : InitialActiveClaimedOrderId
            );
    }
}
