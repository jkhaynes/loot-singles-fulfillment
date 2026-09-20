using LootSingles.Application.Orders;
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
            Result = PickingResult.Success(DetailWithStatus(OrderStatus.Picked)),
        };
        var service = NewService(repository);

        var result = await service.RecordPickedAsync(
            orderId: 3,
            orderLineId: 7,
            actorEmployeeId: 1,
            CancellationToken.None
        );

        Assert.Equal(PickingOutcome.Success, result.Outcome);
        Assert.Equal(OrderStatus.Picked, result.Order!.Status);
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
        Assert.Null(result.Order);
    }

    // 015-pick-completion T027: reporting an issue instead of a false confirmation (US2).
    [Fact]
    public async Task ReportIssueAsync_ClaimHolder_RecordsTheIssueAndReturnsNeedsAttention()
    {
        var repository = new FakePickingRepository
        {
            Result = PickingResult.Success(DetailWithStatus(OrderStatus.NeedsAttention)),
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
        Assert.Equal(OrderStatus.NeedsAttention, result.Order!.Status);
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
            Result = PickingResult.Success(DetailWithStatus(OrderStatus.NeedsAttention)),
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

    // 015-pick-completion T052 (branch review BR-001): the note length and quantity bounds are
    // enforced server-side, not only by the textarea's maxLength and the number inputs' min.
    [Fact]
    public async Task ReportIssueAsync_NoteLongerThanTheColumn_ReturnsInvalidIssueDetails()
    {
        var repository = new FakePickingRepository
        {
            Result = PickingResult.Success(DetailWithStatus(OrderStatus.NeedsAttention)),
        };
        var service = NewService(repository);

        var result = await service.ReportIssueAsync(
            orderId: 3,
            orderLineId: 7,
            actorEmployeeId: 1,
            PickingIssueType.Other,
            requiredQuantity: null,
            foundQuantity: null,
            new string('x', PickingIssue.NoteMaxLength + 1),
            CancellationToken.None
        );

        Assert.Equal(PickingOutcome.InvalidIssueDetails, result.Outcome);
        Assert.Empty(repository.Calls);
    }

    [Fact]
    public async Task ReportIssueAsync_NoteExactlyAtTheLimit_IsAccepted()
    {
        var repository = new FakePickingRepository
        {
            Result = PickingResult.Success(DetailWithStatus(OrderStatus.NeedsAttention)),
        };
        var service = NewService(repository);

        var result = await service.ReportIssueAsync(
            orderId: 3,
            orderLineId: 7,
            actorEmployeeId: 1,
            PickingIssueType.Other,
            requiredQuantity: null,
            foundQuantity: null,
            new string('x', PickingIssue.NoteMaxLength),
            CancellationToken.None
        );

        Assert.Equal(PickingOutcome.Success, result.Outcome);
        Assert.Single(repository.Calls);
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(null, -1)]
    public async Task ReportIssueAsync_NegativeQuantity_ReturnsInvalidIssueDetails(
        int? requiredQuantity,
        int? foundQuantity
    )
    {
        var repository = new FakePickingRepository
        {
            Result = PickingResult.Success(DetailWithStatus(OrderStatus.NeedsAttention)),
        };
        var service = NewService(repository);

        var result = await service.ReportIssueAsync(
            orderId: 3,
            orderLineId: 7,
            actorEmployeeId: 1,
            PickingIssueType.InsufficientQuantity,
            requiredQuantity,
            foundQuantity,
            note: null,
            CancellationToken.None
        );

        Assert.Equal(PickingOutcome.InvalidIssueDetails, result.Outcome);
        Assert.Empty(repository.Calls);
    }

    private static PickingService NewService(IPickingRepository repository) =>
        new(repository, NullLogger<PickingService>.Instance);

    private static OrderDetail DetailWithStatus(OrderStatus status) =>
        new(3, "ORDER-3", status, []);

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
