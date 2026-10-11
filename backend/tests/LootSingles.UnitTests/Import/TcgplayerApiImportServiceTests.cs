using LootSingles.Application.Import;
using LootSingles.Domain.Orders;
using Microsoft.Extensions.Logging.Abstractions;

namespace LootSingles.UnitTests.Import;

/// <summary>
/// T030: the API import orchestration over a fake <see cref="ITcgplayerOrderFeed"/> and an
/// in-memory <see cref="IImportPersistence"/>. All order numbers are synthetic.
/// </summary>
public sealed class TcgplayerApiImportServiceTests
{
    [Fact]
    public async Task Creates_an_api_attempt_whose_detected_count_is_the_number_of_open_orders()
    {
        var feed = new FakeFeed(Numbers(1, 3));
        var persistence = new FakePersistence();

        var updates = await ImportAllAsync(CreateService(feed, persistence));

        var attempt = Assert.Single(persistence.Attempts);
        Assert.Equal(OrderImportSource.TcgplayerApi, attempt.Source);
        Assert.NotNull(attempt.CompletedAt);
        var final = updates[^1];
        Assert.True(final.IsComplete);
        Assert.Same(attempt, final.ImportAttempt);
        Assert.Equal(3, final.OrdersDetected);
        Assert.Equal(3, final.OrdersProcessed);
        Assert.Equal(3, final.SucceededCount);
        Assert.Equal(0, final.FailedCount);
        Assert.Null(attempt.AttemptFailureCode);
    }

    [Fact]
    public async Task Already_imported_orders_are_reported_as_duplicates_and_never_fetched()
    {
        var feed = new FakeFeed(Numbers(1, 3));
        var persistence = new FakePersistence(existing: [Number(2)]);

        var updates = await ImportAllAsync(CreateService(feed, persistence));

        Assert.DoesNotContain(Number(2), feed.Fetched.SelectMany(batch => batch));
        var attempt = updates[^1].ImportAttempt;
        var duplicate = Assert.Single(
            attempt.ImportOrderResults,
            result => result.SourceOrderIdentifier == Number(2)
        );
        Assert.Equal(ImportOutcome.Rejected, duplicate.Outcome);
        Assert.Equal(FailureType.DuplicateOrder, duplicate.FailureCode);
        Assert.Equal("Already imported", duplicate.FailureMessage);
        Assert.Null(duplicate.ResultingOrderId);
        Assert.Equal(3, updates[^1].OrdersDetected);
        Assert.Equal(3, updates[^1].OrdersProcessed);
        Assert.Equal(2, updates[^1].SucceededCount);
        Assert.Equal(1, updates[^1].FailedCount);
    }

    [Fact]
    public async Task When_every_open_order_is_already_imported_nothing_is_fetched()
    {
        var feed = new FakeFeed(Numbers(1, 2));
        var persistence = new FakePersistence(existing: Numbers(1, 2));

        var updates = await ImportAllAsync(CreateService(feed, persistence));

        Assert.Empty(feed.Fetched);
        Assert.All(
            updates[^1].ImportAttempt.ImportOrderResults,
            result => Assert.Equal(FailureType.DuplicateOrder, result.FailureCode)
        );
        Assert.Equal(0, updates[^1].SucceededCount);
        Assert.Equal(2, updates[^1].FailedCount);
    }

    [Fact]
    public async Task New_orders_are_fetched_in_batches_of_the_feed_page_size()
    {
        var feed = new FakeFeed(Numbers(1, 7), pageSize: 3);
        var persistence = new FakePersistence(existing: [Number(4)]);

        await ImportAllAsync(CreateService(feed, persistence));

        Assert.Equal(
            [
                [Number(1), Number(2), Number(3)],
                [Number(5), Number(6), Number(7)],
            ],
            feed.Fetched
        );
    }

    [Fact]
    public async Task Each_new_order_is_imported_as_an_api_order_without_a_packing_slip()
    {
        var feed = new FakeFeed(Numbers(1, 2));
        var persistence = new FakePersistence();

        var updates = await ImportAllAsync(CreateService(feed, persistence));

        Assert.Equal([Number(1), Number(2)], persistence.Saved.Select(o => o.TcgplayerOrderId));
        Assert.All(
            persistence.Saved,
            order =>
            {
                Assert.Equal(OrderImportSource.TcgplayerApi, order.ImportSource);
                Assert.Null(order.PackingSlip);
            }
        );
        Assert.All(
            updates[^1].ImportAttempt.ImportOrderResults,
            result => Assert.Equal(ImportOutcome.Succeeded, result.Outcome)
        );
    }

    [Fact]
    public async Task A_progress_update_follows_each_order_and_then_a_final_one()
    {
        var feed = new FakeFeed(Numbers(1, 3));
        var persistence = new FakePersistence(existing: [Number(1)]);

        var updates = await ImportAllAsync(CreateService(feed, persistence));

        Assert.Equal([1, 2, 3, 3], updates.Select(update => update.OrdersProcessed));
        Assert.Equal([false, false, false, true], updates.Select(update => update.IsComplete));
        Assert.All(updates, update => Assert.Equal(3, update.OrdersDetected));
    }

    [Fact]
    public async Task A_rejected_candidate_is_counted_as_failed_and_its_siblings_still_import()
    {
        var feed = new FakeFeed(Numbers(1, 2))
        {
            Rejections =
            {
                [Number(1)] = (FailureType.IncompleteOrder, "TCGplayer returned 1 of 2 lines"),
            },
        };
        var persistence = new FakePersistence();

        var updates = await ImportAllAsync(CreateService(feed, persistence));

        Assert.Equal([Number(2)], persistence.Saved.Select(order => order.TcgplayerOrderId));
        Assert.Equal(1, updates[^1].SucceededCount);
        Assert.Equal(1, updates[^1].FailedCount);
        Assert.Null(updates[^1].ImportAttempt.AttemptFailureCode);
    }

    [Fact]
    public async Task Zero_open_orders_completes_with_nothing_detected()
    {
        var feed = new FakeFeed([]);
        var persistence = new FakePersistence();

        var updates = await ImportAllAsync(CreateService(feed, persistence));

        var final = Assert.Single(updates);
        Assert.True(final.IsComplete);
        Assert.Equal(0, final.OrdersDetected);
        Assert.Equal(0, final.OrdersProcessed);
        Assert.Empty(final.ImportAttempt.ImportOrderResults);
        Assert.Null(final.ImportAttempt.AttemptFailureCode);
        Assert.NotNull(final.ImportAttempt.CompletedAt);
        Assert.Empty(feed.Fetched);
    }

    [Theory]
    [InlineData(
        TcgplayerFeedFailure.NotConfigured,
        FailureType.TcgplayerNotConfigured,
        "Getting orders from TCGplayer isn't set up here. Use packing-slip upload instead."
    )]
    [InlineData(
        TcgplayerFeedFailure.Unavailable,
        FailureType.TcgplayerUnavailable,
        "Couldn't reach TCGplayer. Orders already imported are kept. Try again in a few minutes, or upload a packing slip."
    )]
    [InlineData(
        TcgplayerFeedFailure.AccessRefused,
        FailureType.TcgplayerAccessRefused,
        "TCGplayer refused the store's connection. A manager needs to check the TCGplayer API setup. You can upload a packing slip meanwhile."
    )]
    public async Task A_listing_failure_fails_the_attempt_with_the_contract_message(
        TcgplayerFeedFailure failure,
        FailureType expectedType,
        string expectedMessage
    )
    {
        var feed = new FakeFeed(Numbers(1, 2))
        {
            ListingFailure = new TcgplayerFeedException(failure, "internal detail"),
        };
        var persistence = new FakePersistence();

        var updates = await ImportAllAsync(CreateService(feed, persistence));

        var final = Assert.Single(updates);
        Assert.True(final.IsComplete);
        Assert.Equal(expectedType, final.ImportAttempt.AttemptFailureCode);
        Assert.Equal(expectedMessage, final.ImportAttempt.AttemptFailureMessage);
        Assert.Equal(0, final.OrdersDetected);
        Assert.NotNull(final.ImportAttempt.CompletedAt);
        Assert.Empty(feed.Fetched);
    }

    [Fact]
    public async Task A_response_invalid_failure_carries_the_feeds_specific_message()
    {
        var feed = new FakeFeed(Numbers(1, 2))
        {
            ListingFailure = new TcgplayerFeedException(
                TcgplayerFeedFailure.ResponseInvalid,
                "The order manifest has no status named 'Ready To Ship'."
            ),
        };

        var updates = await ImportAllAsync(CreateService(feed, new FakePersistence()));

        var attempt = updates[^1].ImportAttempt;
        Assert.Equal(FailureType.TcgplayerResponseInvalid, attempt.AttemptFailureCode);
        Assert.Equal(
            "The order manifest has no status named 'Ready To Ship'.",
            attempt.AttemptFailureMessage
        );
    }

    [Fact]
    public async Task A_fetch_failure_keeps_committed_orders_and_ends_the_stream()
    {
        var feed = new FakeFeed(Numbers(1, 5), pageSize: 2) { FailOnFetch = 2 };
        var persistence = new FakePersistence();

        var updates = await ImportAllAsync(CreateService(feed, persistence));

        Assert.Equal([Number(1), Number(2)], persistence.Saved.Select(o => o.TcgplayerOrderId));
        var final = updates[^1];
        Assert.True(final.IsComplete);
        Assert.Equal(FailureType.TcgplayerUnavailable, final.ImportAttempt.AttemptFailureCode);
        Assert.Equal(5, final.OrdersDetected);
        Assert.Equal(2, final.OrdersProcessed);
        Assert.Equal(2, final.SucceededCount);
        Assert.Equal(2, feed.Fetched.Count);
        Assert.NotNull(final.ImportAttempt.CompletedAt);
    }

    [Fact]
    public async Task A_retry_after_a_failure_imports_the_rest_without_duplicating()
    {
        var persistence = new FakePersistence();
        var failing = new FakeFeed(Numbers(1, 4), pageSize: 2) { FailOnFetch = 2 };
        await ImportAllAsync(CreateService(failing, persistence));

        var retry = new FakeFeed(Numbers(1, 4), pageSize: 2);
        var updates = await ImportAllAsync(CreateService(retry, persistence));

        Assert.Equal(
            [
                [Number(3), Number(4)],
            ],
            retry.Fetched
        );
        Assert.Equal(
            Numbers(1, 4),
            persistence.Saved.Select(order => order.TcgplayerOrderId).ToList()
        );
        Assert.Equal(2, updates[^1].SucceededCount);
        Assert.Equal(2, updates[^1].FailedCount);
        Assert.Null(updates[^1].ImportAttempt.AttemptFailureCode);
    }

    [Fact]
    public async Task Cancellation_stops_before_the_next_order()
    {
        var feed = new FakeFeed(Numbers(1, 3));
        var persistence = new FakePersistence();
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(feed, persistence);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in service.ImportAsync(cancellation.Token))
            {
                cancellation.Cancel();
            }
        });

        Assert.Equal([Number(1)], persistence.Saved.Select(order => order.TcgplayerOrderId));
    }

    private static TcgplayerApiImportService CreateService(
        FakeFeed feed,
        FakePersistence persistence
    ) =>
        new(
            feed,
            new OrderImporter(persistence, NullLogger<OrderImporter>.Instance),
            persistence,
            NullLogger<TcgplayerApiImportService>.Instance
        );

    private static async Task<List<ImportProgressUpdate>> ImportAllAsync(
        TcgplayerApiImportService service
    )
    {
        var updates = new List<ImportProgressUpdate>();
        await foreach (var update in service.ImportAsync(CancellationToken.None))
        {
            updates.Add(update);
        }

        return updates;
    }

    private static string Number(int index) => $"SYN-{index:D4}-A1";

    private static List<string> Numbers(int first, int last) =>
        Enumerable.Range(first, last - first + 1).Select(Number).ToList();

    private sealed class FakeFeed(IReadOnlyList<string> openOrders, int pageSize = 50)
        : ITcgplayerOrderFeed
    {
        public long CallCount { get; private set; } = 1_000;

        public int PageSize => pageSize;

        public TcgplayerFeedException? ListingFailure { get; init; }

        /// <summary>The 1-based fetch call that throws <see cref="TcgplayerFeedFailure.Unavailable"/>.</summary>
        public int? FailOnFetch { get; init; }

        public Dictionary<string, (FailureType, string)> Rejections { get; } = [];

        public List<List<string>> Fetched { get; } = [];

        public Task<IReadOnlyList<string>> GetOpenOrderNumbersAsync(
            CancellationToken cancellationToken
        )
        {
            CallCount += 2;
            return ListingFailure is null
                ? Task.FromResult(openOrders)
                : Task.FromException<IReadOnlyList<string>>(ListingFailure);
        }

        public Task<IReadOnlyList<OrderCandidate>> GetOrdersAsync(
            IReadOnlyList<string> orderNumbers,
            CancellationToken cancellationToken
        )
        {
            CallCount += 3;
            Fetched.Add([.. orderNumbers]);
            if (FailOnFetch == Fetched.Count)
            {
                return Task.FromException<IReadOnlyList<OrderCandidate>>(
                    new TcgplayerFeedException(TcgplayerFeedFailure.Unavailable, "timed out")
                );
            }

            IReadOnlyList<OrderCandidate> candidates = orderNumbers
                .Select(number =>
                    Rejections.TryGetValue(number, out var rejection)
                        ? new OrderCandidate(number, [], rejection)
                        : new OrderCandidate(number, [Line()])
                )
                .ToList();
            return Task.FromResult(candidates);
        }

        private static OrderLineCandidate Line() =>
            new(
                RawDescription: "Synthetic Card",
                ProductLine: "Magic",
                ProductName: "Synthetic Card",
                Set: "Synthetic Set",
                CollectorNumber: null,
                Rarity: null,
                Condition: "Near Mint",
                Variant: null,
                Language: "English",
                ImageUrl: null,
                Quantity: 1
            );
    }

    private sealed class FakePersistence(IEnumerable<string>? existing = null) : IImportPersistence
    {
        private readonly HashSet<string> _persistedOrderIds = new(
            existing ?? [],
            StringComparer.Ordinal
        );
        private readonly List<Order> _pendingOrders = [];

        public List<ImportAttempt> Attempts { get; } = [];

        public List<Order> Saved { get; } = [];

        public void AddImportAttempt(ImportAttempt attempt) => Attempts.Add(attempt);

        public void AddOrder(Order order) => _pendingOrders.Add(order);

        public void DiscardOrder(Order order) => _pendingOrders.Remove(order);

        public Task<bool> OrderExistsAsync(
            string tcgplayerOrderId,
            CancellationToken cancellationToken
        ) => Task.FromResult(_persistedOrderIds.Contains(tcgplayerOrderId));

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            foreach (var order in _pendingOrders)
            {
                _persistedOrderIds.Add(order.TcgplayerOrderId);
                Saved.Add(order);
            }

            _pendingOrders.Clear();
            return Task.CompletedTask;
        }
    }
}
