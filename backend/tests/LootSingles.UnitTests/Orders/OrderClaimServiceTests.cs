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

    [Fact]
    public async Task ClaimAsync_OrderAvailable_ReturnsSuccessWithClaimedOrder()
    {
        var claimedOrder = NewOrder(5, "ORDER-5");
        var repository = new FakeOrderRepository { ClaimSpecificResult = new(true, claimedOrder) };
        var service = NewService(repository);

        var result = await service.ClaimAsync(
            orderId: 5,
            actorEmployeeId: 1,
            CancellationToken.None
        );

        Assert.Equal(OrderClaimOutcome.Success, result.Outcome);
        Assert.Same(claimedOrder, result.Order);
    }

    [Fact]
    public async Task ClaimAsync_OrderDoesNotExist_ReturnsOrderNotFound()
    {
        var repository = new FakeOrderRepository { ClaimSpecificResult = new(false, null) };
        var service = NewService(repository);

        var result = await service.ClaimAsync(
            orderId: 999,
            actorEmployeeId: 1,
            CancellationToken.None
        );

        Assert.Equal(OrderClaimOutcome.OrderNotFound, result.Outcome);
    }

    [Fact]
    public async Task ClaimAsync_OrderAlreadyClaimedByAnother_ReturnsAlreadyClaimedWithExistingClaimant()
    {
        var existingClaim = NewOrder(5, "ORDER-5");
        var repository = new FakeOrderRepository
        {
            ClaimSpecificResult = new(false, existingClaim),
        };
        var service = NewService(repository);

        var result = await service.ClaimAsync(
            orderId: 5,
            actorEmployeeId: 1,
            CancellationToken.None
        );

        Assert.Equal(OrderClaimOutcome.AlreadyClaimed, result.Outcome);
        Assert.Same(existingClaim, result.Order);
    }

    [Fact]
    public async Task ClaimAsync_EmployeeAlreadyHasActiveClaim_ReturnsEmployeeHasActiveClaimWithoutAttemptingClaim()
    {
        var repository = new FakeOrderRepository
        {
            InitialActiveClaimedOrderId = 42,
            ClaimSpecificResult = new(true, NewOrder(5, "ORDER-5")),
        };
        var service = NewService(repository);

        var result = await service.ClaimAsync(
            orderId: 5,
            actorEmployeeId: 1,
            CancellationToken.None
        );

        Assert.Equal(OrderClaimOutcome.EmployeeHasActiveClaim, result.Outcome);
        Assert.Equal(42, result.ConflictingOrderId);
        Assert.False(repository.ClaimSpecificCalled);
    }

    [Fact]
    public async Task ReleaseAsync_ClaimantReleases_ReturnsSuccessWithReleasedOrder()
    {
        var releasedOrder = NewOrder(5, "ORDER-5");
        var repository = new FakeOrderRepository { ReleaseResult = new(true, releasedOrder) };
        var service = NewService(repository);

        var result = await service.ReleaseAsync(
            orderId: 5,
            actorEmployeeId: 1,
            CancellationToken.None
        );

        Assert.Equal(OrderClaimOutcome.Success, result.Outcome);
        Assert.Same(releasedOrder, result.Order);
    }

    [Fact]
    public async Task ReleaseAsync_OrderDoesNotExist_ReturnsOrderNotFound()
    {
        var repository = new FakeOrderRepository { ReleaseResult = new(false, null) };
        var service = NewService(repository);

        var result = await service.ReleaseAsync(
            orderId: 999,
            actorEmployeeId: 1,
            CancellationToken.None
        );

        Assert.Equal(OrderClaimOutcome.OrderNotFound, result.Outcome);
    }

    [Fact]
    public async Task ReleaseAsync_OrderNotCurrentlyClaimed_ReturnsNotYourClaim()
    {
        var unclaimedOrder = NewOrder(5, "ORDER-5");
        var repository = new FakeOrderRepository { ReleaseResult = new(false, unclaimedOrder) };
        var service = NewService(repository);

        var result = await service.ReleaseAsync(
            orderId: 5,
            actorEmployeeId: 1,
            CancellationToken.None
        );

        Assert.Equal(OrderClaimOutcome.NotYourClaim, result.Outcome);
    }

    [Fact]
    public async Task ReleaseAsync_OrderClaimedByAnotherEmployee_ReturnsNotYourClaim()
    {
        var claimedByOther = NewOrder(5, "ORDER-5");
        var repository = new FakeOrderRepository { ReleaseResult = new(false, claimedByOther) };
        var service = NewService(repository);

        var result = await service.ReleaseAsync(
            orderId: 5,
            actorEmployeeId: 1,
            CancellationToken.None
        );

        Assert.Equal(OrderClaimOutcome.NotYourClaim, result.Outcome);
    }

    [Fact]
    public async Task ForceReleaseAsync_OrderClaimed_ReturnsSuccessWithReleasedOrder()
    {
        var releasedOrder = NewOrder(5, "ORDER-5");
        var repository = new FakeOrderRepository { ForceReleaseResult = new(true, releasedOrder) };
        var service = NewService(repository);

        var result = await service.ForceReleaseAsync(
            orderId: 5,
            actorEmployeeId: 9,
            CancellationToken.None
        );

        Assert.Equal(OrderClaimOutcome.Success, result.Outcome);
        Assert.Same(releasedOrder, result.Order);
    }

    [Fact]
    public async Task ForceReleaseAsync_OrderDoesNotExist_ReturnsOrderNotFound()
    {
        var repository = new FakeOrderRepository { ForceReleaseResult = new(false, null) };
        var service = NewService(repository);

        var result = await service.ForceReleaseAsync(
            orderId: 999,
            actorEmployeeId: 9,
            CancellationToken.None
        );

        Assert.Equal(OrderClaimOutcome.OrderNotFound, result.Outcome);
    }

    [Fact]
    public async Task ForceReleaseAsync_OrderNotCurrentlyClaimed_ReturnsOrderNotClaimed()
    {
        var unclaimedOrder = NewOrder(5, "ORDER-5");
        var repository = new FakeOrderRepository
        {
            ForceReleaseResult = new(false, unclaimedOrder),
        };
        var service = NewService(repository);

        var result = await service.ForceReleaseAsync(
            orderId: 5,
            actorEmployeeId: 9,
            CancellationToken.None
        );

        Assert.Equal(OrderClaimOutcome.OrderNotClaimed, result.Outcome);
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
        public ClaimAttemptResult ClaimSpecificResult { get; set; } = new(false, null);
        public bool ClaimSpecificCalled { get; private set; }
        public ClaimAttemptResult ReleaseResult { get; set; } = new(false, null);
        public ClaimAttemptResult ForceReleaseResult { get; set; } = new(false, null);

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

        public Task<ClaimAttemptResult> ClaimSpecificAsync(
            int orderId,
            int actorEmployeeId,
            CancellationToken cancellationToken
        )
        {
            ClaimSpecificCalled = true;
            return Task.FromResult(ClaimSpecificResult);
        }

        public Task<ClaimAttemptResult> ReleaseAsync(
            int orderId,
            int actorEmployeeId,
            CancellationToken cancellationToken
        ) => Task.FromResult(ReleaseResult);

        public Task<ClaimAttemptResult> ForceReleaseAsync(
            int orderId,
            CancellationToken cancellationToken
        ) => Task.FromResult(ForceReleaseResult);
    }
}
