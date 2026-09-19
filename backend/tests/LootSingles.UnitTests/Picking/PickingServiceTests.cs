using LootSingles.Application.Picking;
using LootSingles.Domain.Orders;
using Microsoft.Extensions.Logging.Abstractions;

namespace LootSingles.UnitTests.Picking;

public sealed class PickingServiceTests
{
    [Fact]
    public async Task RecordPickedAsync_ClaimHolder_RecordsPickedAndReturnsDerivedStatus()
    {
        var repository = new FakePickingRepository
        {
            Result = PickingResult.Success(OrderStatus.Picked),
        };
        var service = NewService(repository);

        var result = await service.RecordPickedAsync(
            orderId: 3,
            orderLineId: 7,
            actorEmployeeId: 1,
            CancellationToken.None
        );

        Assert.Equal(PickingOutcome.Success, result.Outcome);
        Assert.Equal(OrderStatus.Picked, result.OrderStatus);
        var call = Assert.Single(repository.Calls);
        Assert.Equal((3, 7, 1), (call.OrderId, call.OrderLineId, call.ActorEmployeeId));
        Assert.IsType<PickOutcomeChange.Picked>(call.Change);
    }

    [Fact]
    public async Task RecordPickedAsync_ActorDoesNotHoldClaim_ReturnsNotYourClaim()
    {
        var repository = new FakePickingRepository { Result = PickingResult.NotYourClaim };
        var service = NewService(repository);

        var result = await service.RecordPickedAsync(
            orderId: 3,
            orderLineId: 7,
            actorEmployeeId: 2,
            CancellationToken.None
        );

        Assert.Equal(PickingOutcome.NotYourClaim, result.Outcome);
        Assert.Null(result.OrderStatus);
    }

    // 015-pick-completion T027: reporting an issue instead of a false confirmation (US2).
    [Fact]
    public async Task ReportIssueAsync_ClaimHolder_RecordsTheIssueAndReturnsNeedsAttention()
    {
        var repository = new FakePickingRepository
        {
            Result = PickingResult.Success(OrderStatus.NeedsAttention),
        };
        var service = NewService(repository);

        var result = await service.ReportIssueAsync(
            orderId: 3,
            orderLineId: 7,
            actorEmployeeId: 1,
            PickingIssueType.CardNotFound,
            requiredQuantity: 2,
            foundQuantity: 1,
            note: "Only one copy in the bin",
            CancellationToken.None
        );

        Assert.Equal(PickingOutcome.Success, result.Outcome);
        Assert.Equal(OrderStatus.NeedsAttention, result.OrderStatus);
        var report = Assert.IsType<PickOutcomeChange.IssueReport>(
            Assert.Single(repository.Calls).Change
        );
        Assert.Equal(PickingIssueType.CardNotFound, report.IssueType);
        Assert.Equal(2, report.RequiredQuantity);
        Assert.Equal(1, report.FoundQuantity);
        Assert.Equal("Only one copy in the bin", report.Note);
    }

    [Fact]
    public async Task ReportIssueAsync_UnrecognizedIssueType_ReturnsInvalidIssueTypeWithoutWriting()
    {
        var repository = new FakePickingRepository
        {
            Result = PickingResult.Success(OrderStatus.NeedsAttention),
        };
        var service = NewService(repository);

        var result = await service.ReportIssueAsync(
            orderId: 3,
            orderLineId: 7,
            actorEmployeeId: 1,
            (PickingIssueType)999,
            requiredQuantity: null,
            foundQuantity: null,
            note: null,
            CancellationToken.None
        );

        Assert.Equal(PickingOutcome.InvalidIssueType, result.Outcome);
        Assert.Empty(repository.Calls);
    }

    [Fact]
    public async Task ReportIssueAsync_ActorDoesNotHoldClaim_ReturnsNotYourClaim()
    {
        var repository = new FakePickingRepository { Result = PickingResult.NotYourClaim };
        var service = NewService(repository);

        var result = await service.ReportIssueAsync(
            orderId: 3,
            orderLineId: 7,
            actorEmployeeId: 2,
            PickingIssueType.Damaged,
            requiredQuantity: null,
            foundQuantity: null,
            note: null,
            CancellationToken.None
        );

        Assert.Equal(PickingOutcome.NotYourClaim, result.Outcome);
    }

    private static PickingService NewService(IPickingRepository repository) =>
        new(repository, NullLogger<PickingService>.Instance);

    private sealed class FakePickingRepository : IPickingRepository
    {
        public required PickingResult Result { get; init; }

        public List<(
            int OrderId,
            int OrderLineId,
            int ActorEmployeeId,
            PickOutcomeChange Change
        )> Calls { get; } = [];

        public Task<PickingResult> RecordOutcomeAsync(
            int orderId,
            int orderLineId,
            int actorEmployeeId,
            PickOutcomeChange change,
            CancellationToken cancellationToken
        )
        {
            Calls.Add((orderId, orderLineId, actorEmployeeId, change));
            return Task.FromResult(Result);
        }
    }
}
