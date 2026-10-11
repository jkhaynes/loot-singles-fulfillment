using LootSingles.Application.Orders;
using LootSingles.UnitTests.CardCatalog;
using Microsoft.Extensions.Logging;

namespace LootSingles.UnitTests.Orders;

public sealed class OrderDeletionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(OrderDeletionOutcome.Deleted)]
    [InlineData(OrderDeletionOutcome.NotFound)]
    [InlineData(OrderDeletionOutcome.AlreadyDeleted)]
    [InlineData(OrderDeletionOutcome.ClaimedBefore)]
    public async Task DeleteAsync_MapsRepositoryOutcome(OrderDeletionOutcome outcome)
    {
        var repository = new FakeRepository
        {
            DeleteResult = new DeleteOrderAttempt(outcome, "SYN-1"),
        };
        var (service, _) = NewService(repository);

        var result = await service.DeleteAsync(42, managerId: 7, CancellationToken.None);

        Assert.Equal(outcome, result.Outcome);
    }

    [Fact]
    public async Task DeleteAsync_PassesActorOrderTimeAndDefaultsAllowTouchedToFalse()
    {
        var repository = new FakeRepository();
        var (service, _) = NewService(repository);

        await service.DeleteAsync(42, managerId: 7, CancellationToken.None);

        Assert.Equal(42, repository.DeleteOrderId);
        Assert.Equal(7, repository.DeleteActorId);
        Assert.Equal(Now, repository.DeleteNow);
        Assert.False(repository.DeleteAllowTouched);
        Assert.False(new OrderDeletionOptions().AllowTouchedOrders);
    }

    [Fact]
    public async Task DeleteAsync_PassesAllowTouchedFromOptions()
    {
        var repository = new FakeRepository();
        var (service, _) = NewService(
            repository,
            new OrderDeletionOptions { AllowTouchedOrders = true }
        );

        await service.DeleteAsync(42, managerId: 7, CancellationToken.None);

        Assert.True(repository.DeleteAllowTouched);
    }

    [Fact]
    public async Task DeleteAsync_Deleted_LogsOneInformationEntryWithSafeFields()
    {
        var repository = new FakeRepository
        {
            DeleteResult = new DeleteOrderAttempt(OrderDeletionOutcome.Deleted, "SYN-0002-A1"),
        };
        var (service, logger) = NewService(repository);

        await service.DeleteAsync(42, managerId: 7, CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Manager 7 deleted order 42 (SYN-0002-A1).", entry.Message);
        Assert.Equal(7, entry.Fields["ManagerId"]);
        Assert.Equal(42, entry.Fields["OrderId"]);
        Assert.Equal("SYN-0002-A1", entry.Fields["TcgplayerOrderId"]);
        Assert.Equal(
            ["ManagerId", "OrderId", "TcgplayerOrderId"],
            entry.Fields.Keys.Where(k => k != "{OriginalFormat}").Order()
        );
    }

    [Theory]
    [InlineData(OrderDeletionOutcome.NotFound)]
    [InlineData(OrderDeletionOutcome.AlreadyDeleted)]
    [InlineData(OrderDeletionOutcome.ClaimedBefore)]
    public async Task DeleteAsync_Refused_LogsNothing(OrderDeletionOutcome outcome)
    {
        var repository = new FakeRepository
        {
            DeleteResult = new DeleteOrderAttempt(outcome, null),
        };
        var (service, logger) = NewService(repository);

        await service.DeleteAsync(42, managerId: 7, CancellationToken.None);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task DeleteAllAsync_ReturnsCountsAndPassesArguments()
    {
        var asOf = Now.AddMinutes(-5);
        var repository = new FakeRepository { DeleteAllResult = new DeleteAllCounts(2, 4) };
        var (service, _) = NewService(
            repository,
            new OrderDeletionOptions { AllowTouchedOrders = true }
        );

        var result = await service.DeleteAllAsync(managerId: 7, asOf, CancellationToken.None);

        Assert.Equal(OrderDeleteAllOutcome.Done, result.Outcome);
        Assert.Equal(2, result.DeletedCount);
        Assert.Equal(4, result.KeptCount);
        Assert.Equal(7, repository.DeleteAllActorId);
        Assert.Equal(Now, repository.DeleteAllNow);
        Assert.Equal(asOf, repository.DeleteAllAsOf);
        Assert.True(repository.DeleteAllAllowTouched);
    }

    [Fact]
    public async Task DeleteAllAsync_LogsOneSummaryEntry()
    {
        var repository = new FakeRepository { DeleteAllResult = new DeleteAllCounts(2, 4) };
        var (service, logger) = NewService(repository);

        await service.DeleteAllAsync(managerId: 7, Now, CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Manager 7 deleted 2 orders; 4 kept.", entry.Message);
        Assert.Equal(
            ["DeletedCount", "KeptCount", "ManagerId"],
            entry.Fields.Keys.Where(k => k != "{OriginalFormat}").Order()
        );
    }

    [Fact]
    public async Task DeleteAllAsync_ZeroDeleted_StillDoneAndLogged()
    {
        var repository = new FakeRepository { DeleteAllResult = new DeleteAllCounts(0, 3) };
        var (service, logger) = NewService(repository);

        var result = await service.DeleteAllAsync(7, Now, CancellationToken.None);

        Assert.Equal(OrderDeleteAllOutcome.Done, result.Outcome);
        Assert.Single(logger.Entries);
    }

    [Fact]
    public async Task DeleteAllAsync_AsOfInTheFuture_IsInvalidAndTouchesNothing()
    {
        var repository = new FakeRepository();
        var (service, logger) = NewService(repository);

        var result = await service.DeleteAllAsync(7, Now.AddTicks(1), CancellationToken.None);

        Assert.Equal(OrderDeleteAllOutcome.InvalidAsOf, result.Outcome);
        Assert.False(repository.DeleteAllCalled);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task DeleteAllAsync_AsOfEqualToNow_IsAccepted()
    {
        var (service, _) = NewService(new FakeRepository());

        var result = await service.DeleteAllAsync(7, Now, CancellationToken.None);

        Assert.Equal(OrderDeleteAllOutcome.Done, result.Outcome);
    }

    [Fact]
    public async Task GetSummaryAsync_ReturnsCountsStampedWithServiceNow()
    {
        var repository = new FakeRepository
        {
            SummaryResult = new OrderDeletionCounts(
                DeletableCount: 2,
                KeptCount: 4,
                ClaimedCount: 1,
                WithUnresolvedIssueCount: 1
            ),
        };
        var (service, logger) = NewService(
            repository,
            new OrderDeletionOptions { AllowTouchedOrders = true }
        );

        var summary = await service.GetSummaryAsync(CancellationToken.None);

        Assert.Equal(Now, summary.AsOf);
        Assert.Equal(2, summary.DeletableCount);
        Assert.Equal(4, summary.KeptCount);
        Assert.Equal(1, summary.ClaimedCount);
        Assert.Equal(1, summary.WithUnresolvedIssueCount);
        Assert.Equal(Now, repository.SummaryNow);
        Assert.True(repository.SummaryAllowTouched);
        Assert.Empty(logger.Entries);
    }

    private static (OrderDeletionService Service, CapturingLogger Logger) NewService(
        FakeRepository repository,
        OrderDeletionOptions? options = null
    )
    {
        var logger = new CapturingLogger();
        var service = new OrderDeletionService(
            repository,
            options ?? new OrderDeletionOptions(),
            new FakeTimeProvider(Now),
            logger
        );
        return (service, logger);
    }

    private sealed class FakeRepository : IOrderDeletionRepository
    {
        public DeleteOrderAttempt DeleteResult { get; set; } =
            new(OrderDeletionOutcome.Deleted, "SYN-1");
        public DeleteAllCounts DeleteAllResult { get; set; } = new(0, 0);
        public OrderDeletionCounts SummaryResult { get; set; } = new(0, 0, 0, 0);

        public int DeleteOrderId { get; private set; }
        public int DeleteActorId { get; private set; }
        public DateTimeOffset DeleteNow { get; private set; }
        public bool DeleteAllowTouched { get; private set; }

        public bool DeleteAllCalled { get; private set; }
        public int DeleteAllActorId { get; private set; }
        public DateTimeOffset DeleteAllNow { get; private set; }
        public DateTimeOffset DeleteAllAsOf { get; private set; }
        public bool DeleteAllAllowTouched { get; private set; }

        public DateTimeOffset SummaryNow { get; private set; }
        public bool SummaryAllowTouched { get; private set; }

        public Task<DeleteOrderAttempt> DeleteAsync(
            int orderId,
            int actorEmployeeId,
            DateTimeOffset now,
            bool allowTouched,
            CancellationToken cancellationToken
        )
        {
            DeleteOrderId = orderId;
            DeleteActorId = actorEmployeeId;
            DeleteNow = now;
            DeleteAllowTouched = allowTouched;
            return Task.FromResult(DeleteResult);
        }

        public Task<DeleteAllCounts> DeleteAllAsync(
            int actorEmployeeId,
            DateTimeOffset now,
            DateTimeOffset asOf,
            bool allowTouched,
            CancellationToken cancellationToken
        )
        {
            DeleteAllCalled = true;
            DeleteAllActorId = actorEmployeeId;
            DeleteAllNow = now;
            DeleteAllAsOf = asOf;
            DeleteAllAllowTouched = allowTouched;
            return Task.FromResult(DeleteAllResult);
        }

        public Task<OrderDeletionCounts> GetSummaryAsync(
            DateTimeOffset now,
            bool allowTouched,
            CancellationToken cancellationToken
        )
        {
            SummaryNow = now;
            SummaryAllowTouched = allowTouched;
            return Task.FromResult(SummaryResult);
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> Fields
    );

    private sealed class CapturingLogger : ILogger<OrderDeletionService>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) =>
            Entries.Add(
                new LogEntry(
                    logLevel,
                    formatter(state, exception),
                    (state as IEnumerable<KeyValuePair<string, object?>> ?? []).ToDictionary(
                        p => p.Key,
                        p => p.Value
                    )
                )
            );
    }
}
