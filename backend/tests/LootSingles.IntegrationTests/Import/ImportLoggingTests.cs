using System.Data.Common;
using System.Runtime.CompilerServices;
using LootSingles.Application.Import;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Import;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LootSingles.IntegrationTests.Import;

public class ImportLoggingTests
{
    // Raw card-name text from valid-multi-order-batch.pdf; never a safe identifier, must never leak.
    private const string ProductMarker = "Orim's Chant";

    // Raw card-name text from duplicate-product-line-same-order.pdf; never a safe identifier, must never leak.
    private const string DuplicateFixtureProductMarker = "Genesect ex";

    [Fact]
    public async Task ImportAsync_UnreadableFile_ProducesWarningWithImportIdAndFailureType()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext();
        var logger = new ImportTestSupport.CapturingLogger<PackingSlipImportService>();
        var repository = new ImportRepository(context);
        var service = new PackingSlipImportService(
            new PdfPigPackingSlipParser(),
            new PdfPigPackingSlipSlicer(),
            new OrderImporter(repository, NullLogger<OrderImporter>.Instance),
            repository,
            logger
        );

        var final = await ImportTestSupport.ImportFixtureAsync(service, "corrupted-file.pdf");

        Assert.Equal(FailureType.UnreadablePdf, final.ImportAttempt.AttemptFailureCode);
        Assert.NotEqual(0, final.ImportAttempt.Id);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(final.ImportAttempt.Id, entry.GetState<int>("ImportId"));
        Assert.Equal(OrderImportSource.PackingSlipPdf, entry.GetState<OrderImportSource>("Source"));
        Assert.Equal(FailureType.UnreadablePdf, entry.GetState<FailureType>("AttemptFailureType"));
    }

    [Fact]
    public async Task ImportAsync_DuplicateOrderReimport_ProducesWarningWithDuplicateBreakdown()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext();
        var firstService = ImportTestSupport.CreateService(context);
        await ImportTestSupport.ImportFixtureAsync(firstService, "valid-multi-order-batch.pdf");

        var logger = new ImportTestSupport.CapturingLogger<PackingSlipImportService>();
        var repository = new ImportRepository(context);
        var service = new PackingSlipImportService(
            new PdfPigPackingSlipParser(),
            new PdfPigPackingSlipSlicer(),
            new OrderImporter(repository, NullLogger<OrderImporter>.Instance),
            repository,
            logger
        );
        var final = await ImportTestSupport.ImportFixtureAsync(
            service,
            "valid-multi-order-batch.pdf"
        );

        Assert.NotEqual(0, final.ImportAttempt.Id);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(final.ImportAttempt.Id, entry.GetState<int>("ImportId"));
        Assert.Equal(OrderImportSource.PackingSlipPdf, entry.GetState<OrderImportSource>("Source"));
        Assert.Equal(13, entry.GetState<int>("OrdersDetected"));
        Assert.Equal(0, entry.GetState<int>("OrdersSucceeded"));
        Assert.Equal(13, entry.GetState<int>("OrdersFailed"));
        Assert.Equal(13, entry.GetState<int>("DuplicateOrderCount"));
        var duplicateOrderIds = entry.GetState<string>("DuplicateOrderIds").Split(',');
        Assert.Equal(13, duplicateOrderIds.Length);
        Assert.Contains("F0000010-ABC010-00010", duplicateOrderIds);
        Assert.Contains("DuplicateOrder: 13 [", entry.Message, StringComparison.Ordinal);
        AssertNoLeakedContent(entry, ProductMarker);
    }

    [Fact]
    public async Task ImportAsync_OrderWithMissingIdentifier_ProducesWarningWithMissingIdentifierPlaceholder()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext();
        var logger = new ImportTestSupport.CapturingLogger<PackingSlipImportService>();
        var repository = new ImportRepository(context);
        var service = new PackingSlipImportService(
            new PdfPigPackingSlipParser(),
            new PdfPigPackingSlipSlicer(),
            new OrderImporter(repository, NullLogger<OrderImporter>.Instance),
            repository,
            logger
        );

        var final = await ImportTestSupport.ImportFixtureAsync(
            service,
            "missing-order-identifier.pdf"
        );

        Assert.NotEqual(0, final.ImportAttempt.Id);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(1, entry.GetState<int>("MissingOrderIdentifierCount"));
        Assert.Equal("(missing)", entry.GetState<string>("MissingOrderIdentifierIds"));
        Assert.Contains(
            "MissingOrderIdentifier: 1 [(missing)]",
            entry.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ImportAsync_SummaryMismatchWithNoFailedOrders_ProducesWarningNotInformation()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext();
        var logger = new ImportTestSupport.CapturingLogger<PackingSlipImportService>();
        var repository = new ImportRepository(context);
        var service = new PackingSlipImportService(
            new PdfPigPackingSlipParser(),
            new PdfPigPackingSlipSlicer(),
            new OrderImporter(repository, NullLogger<OrderImporter>.Instance),
            repository,
            logger
        );

        var final = await ImportTestSupport.ImportFixtureAsync(service, "summary-mismatch.pdf");

        Assert.Equal(FailureType.SummaryMismatch, final.ImportAttempt.AttemptFailureCode);
        Assert.Equal(0, final.FailedCount);
        Assert.NotEqual(0, final.ImportAttempt.Id);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(
            FailureType.SummaryMismatch,
            entry.GetState<FailureType>("AttemptFailureType")
        );
    }

    [Fact]
    public async Task ImportAsync_MixedSucceededAndFailedOrders_ProducesWarningWithBothNonZeroCounts()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext();
        var logger = new ImportTestSupport.CapturingLogger<PackingSlipImportService>();
        var repository = new ImportRepository(context);
        var service = new PackingSlipImportService(
            new PdfPigPackingSlipParser(),
            new PdfPigPackingSlipSlicer(),
            new OrderImporter(repository, NullLogger<OrderImporter>.Instance),
            repository,
            logger
        );

        var final = await ImportTestSupport.ImportFixtureAsync(
            service,
            "partial-batch-one-bad-order.pdf"
        );

        Assert.Equal(2, final.SucceededCount);
        Assert.Equal(1, final.FailedCount);
        Assert.NotEqual(0, final.ImportAttempt.Id);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(3, entry.GetState<int>("OrdersDetected"));
        Assert.Equal(2, entry.GetState<int>("OrdersSucceeded"));
        Assert.Equal(1, entry.GetState<int>("OrdersFailed"));
        Assert.Equal(1, entry.GetState<int>("MissingOrderIdentifierCount"));
        Assert.Equal("(missing)", entry.GetState<string>("MissingOrderIdentifierIds"));
        Assert.Contains(
            "MissingOrderIdentifier: 1 [(missing)]",
            entry.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ImportAsync_OrderPersistenceFailure_ProducesErrorWithImportIdOrderIdAndFailureType()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext(
            new FailOrderLineInsertInterceptor()
        );
        var logger = new ImportTestSupport.CapturingLogger<OrderImporter>();
        var repository = new ImportRepository(context);
        var service = new PackingSlipImportService(
            new PdfPigPackingSlipParser(),
            new PdfPigPackingSlipSlicer(),
            new OrderImporter(repository, logger),
            repository,
            NullLogger<PackingSlipImportService>.Instance
        );

        var final = await ImportTestSupport.ImportFixtureAsync(
            service,
            "duplicate-product-line-same-order.pdf"
        );

        Assert.NotEqual(0, final.ImportAttempt.Id);
        var entry = Assert.Single(logger.Entries, candidate => candidate.Level == LogLevel.Error);
        Assert.Equal(final.ImportAttempt.Id, entry.GetState<int>("ImportId"));
        Assert.Equal("DUPLICATE-LINE-FIXTURE", entry.GetState<string>("OrderId"));
        Assert.Equal(FailureType.PersistenceFailure, entry.GetState<FailureType>("FailureType"));
        AssertNoLeakedContent(entry, DuplicateFixtureProductMarker);
    }

    [Fact]
    public async Task ImportAsync_FullySuccessfulImport_ProducesInformationCompletionLogOnly()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext();
        var logger = new ImportTestSupport.CapturingLogger<PackingSlipImportService>();
        var repository = new ImportRepository(context);
        var service = new PackingSlipImportService(
            new PdfPigPackingSlipParser(),
            new PdfPigPackingSlipSlicer(),
            new OrderImporter(repository, NullLogger<OrderImporter>.Instance),
            repository,
            logger
        );

        var final = await ImportTestSupport.ImportFixtureAsync(
            service,
            "valid-multi-order-batch.pdf"
        );

        Assert.NotEqual(0, final.ImportAttempt.Id);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(final.ImportAttempt.Id, entry.GetState<int>("ImportId"));
        Assert.Equal(OrderImportSource.PackingSlipPdf, entry.GetState<OrderImportSource>("Source"));
        Assert.Equal(13, entry.GetState<int>("OrdersDetected"));
        Assert.Equal(13, entry.GetState<int>("OrdersSucceeded"));
        Assert.Null(entry.GetState<int?>("CallCount"));
        AssertNoLeakedContent(entry, ProductMarker);
    }

    [Fact]
    public async Task ApiImport_CompletedAttempt_ProducesOneCompletionLogWithSourceAndCallCount()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext();
        var importerLogger = new ImportTestSupport.CapturingLogger<OrderImporter>();
        var logger = new ImportTestSupport.CapturingLogger<TcgplayerApiImportService>();
        var repository = new ImportRepository(context);
        var prefix = $"SYN-{Guid.NewGuid():N}"[..16];
        var feed = new SyntheticOrderFeed([$"{prefix}-1", $"{prefix}-2", $"{prefix}-3"]);
        var service = new TcgplayerApiImportService(
            feed,
            new OrderImporter(repository, importerLogger),
            repository,
            logger
        );

        ImportProgressUpdate? final = null;
        await foreach (var update in service.ImportAsync())
        {
            final = update;
        }

        Assert.NotNull(final);
        Assert.Empty(importerLogger.Entries);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(final.ImportAttempt.Id, entry.GetState<int>("ImportId"));
        Assert.Equal(OrderImportSource.TcgplayerApi, entry.GetState<OrderImportSource>("Source"));
        Assert.Equal(3, entry.GetState<int>("OrdersDetected"));
        Assert.Equal(3, entry.GetState<int>("OrdersSucceeded"));
        // Listing costs two calls and the one fetch batch three (SyntheticOrderFeed), counted
        // from wherever the process-wide total stood when the attempt began.
        Assert.Equal(5, entry.GetState<int?>("CallCount"));
        AssertNoLeakedContent(entry, SyntheticOrderFeed.ProductName);
    }

    [Fact]
    public async Task ApiImport_OnlyImportedAndAlreadyImported_ProducesOneInformationWithCountAndNoOrderNumbers()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext();
        var repository = new ImportRepository(context);
        var prefix = $"SYN-{Guid.NewGuid():N}"[..16];
        var existing = new[] { $"{prefix}-1", $"{prefix}-2" };
        await RunApiImportAsync(repository, new SyntheticOrderFeed(existing));

        var logger = new ImportTestSupport.CapturingLogger<TcgplayerApiImportService>();
        var feed = new SyntheticOrderFeed([.. existing, $"{prefix}-3"]);
        await RunApiImportAsync(repository, feed, logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(3, entry.GetState<int>("OrdersDetected"));
        Assert.Equal(1, entry.GetState<int>("OrdersSucceeded"));
        Assert.Equal(0, entry.GetState<int>("OrdersFailed"));
        Assert.Equal(2, entry.GetState<int>("OrdersAlreadyImported"));
        Assert.DoesNotContain(prefix, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            entry.State,
            pair => pair.Value is string text && text.Contains(prefix, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task ApiImport_AlreadyImportedPlusRealRejection_ProducesOneWarningBreakdownWithoutDuplicates()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext();
        var repository = new ImportRepository(context);
        var prefix = $"SYN-{Guid.NewGuid():N}"[..16];
        var existing = $"{prefix}-1";
        await RunApiImportAsync(repository, new SyntheticOrderFeed([existing]));

        var logger = new ImportTestSupport.CapturingLogger<TcgplayerApiImportService>();
        var rejected = $"{prefix}-2";
        var feed = new SyntheticOrderFeed([existing, rejected, $"{prefix}-3"])
        {
            NoLineOrders = [rejected],
        };
        await RunApiImportAsync(repository, feed, logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(3, entry.GetState<int>("OrdersDetected"));
        Assert.Equal(1, entry.GetState<int>("OrdersSucceeded"));
        Assert.Equal(1, entry.GetState<int>("OrdersFailed"));
        Assert.Equal(1, entry.GetState<int>("OrdersAlreadyImported"));
        Assert.Equal(1, entry.GetState<int>("NoProductLinesCount"));
        Assert.Equal(rejected, entry.GetState<string>("NoProductLinesIds"));
        Assert.DoesNotContain("DuplicateOrder", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(existing, entry.Message, StringComparison.Ordinal);
    }

    private static async Task RunApiImportAsync(
        ImportRepository repository,
        SyntheticOrderFeed feed,
        ImportTestSupport.CapturingLogger<TcgplayerApiImportService>? logger = null
    )
    {
        var service = new TcgplayerApiImportService(
            feed,
            new OrderImporter(repository, NullLogger<OrderImporter>.Instance),
            repository,
            logger ?? new ImportTestSupport.CapturingLogger<TcgplayerApiImportService>()
        );
        await foreach (var _ in service.ImportAsync()) { }
    }

    [Fact]
    public async Task ApiImport_AttemptWideFailure_ProducesOneWarningWithFailureTypeAndCallCount()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext();
        var logger = new ImportTestSupport.CapturingLogger<TcgplayerApiImportService>();
        var repository = new ImportRepository(context);
        var feed = new SyntheticOrderFeed([]) { Unreachable = true };
        var service = new TcgplayerApiImportService(
            feed,
            new OrderImporter(repository, NullLogger<OrderImporter>.Instance),
            repository,
            logger
        );

        await foreach (var _ in service.ImportAsync()) { }

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(OrderImportSource.TcgplayerApi, entry.GetState<OrderImportSource>("Source"));
        Assert.Equal(
            FailureType.TcgplayerUnavailable,
            entry.GetState<FailureType>("AttemptFailureType")
        );
        Assert.Equal(2, entry.GetState<int?>("CallCount"));
    }

    [Fact]
    public async Task ImportAsync_CancelledMidBatch_ProducesNoWarningOrErrorLogEntry()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext();
        var importerLogger = new ImportTestSupport.CapturingLogger<OrderImporter>();
        var logger = new ImportTestSupport.CapturingLogger<PackingSlipImportService>();
        var repository = new ImportRepository(context);
        var service = new PackingSlipImportService(
            new CancellableParser(),
            new PdfPigPackingSlipSlicer(),
            new OrderImporter(repository, importerLogger),
            repository,
            logger
        );
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in service.ImportAsync(Stream.Null, cts.Token))
            {
                cts.Cancel();
            }
        });

        Assert.DoesNotContain(
            logger.Entries.Concat(importerLogger.Entries),
            entry => entry.Level is LogLevel.Warning or LogLevel.Error
        );
    }

    [Fact]
    public async Task RealDiConfiguredLogger_HasInformationWarningAndErrorEnabled()
    {
        await using var factory = new AuthWebApplicationFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<PackingSlipImportService>>();

        Assert.True(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
        Assert.True(logger.IsEnabled(LogLevel.Error));
    }

    [Fact]
    public async Task ImportAsync_TwoHundredOrderBatch_ProducesOnlyAttemptLevelLogEntries()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext();
        var importerLogger = new ImportTestSupport.CapturingLogger<OrderImporter>();
        var logger = new ImportTestSupport.CapturingLogger<PackingSlipImportService>();
        var repository = new ImportRepository(context);
        var service = new PackingSlipImportService(
            new SyntheticProgressiveParser(200),
            new PdfPigPackingSlipSlicer(),
            new OrderImporter(repository, importerLogger),
            repository,
            logger
        );

        ImportProgressUpdate? final = null;
        await foreach (var update in service.ImportAsync(new MemoryStream([0])))
        {
            final = update;
        }

        Assert.NotNull(final);
        Assert.NotEqual(0, final.ImportAttempt.Id);

        // The point of this test is that logging never scales with the batch. Two hundred
        // orders must not produce two hundred entries — the constitution forbids per-loop
        // logging, and an operator cannot read it anyway. The bound is deliberately tight:
        // a per-order regression would show up here as 200-odd entries.
        var totalEntries = logger.Entries.Count + importerLogger.Entries.Count;
        Assert.True(
            totalEntries <= 2,
            $"expected attempt-level logging only, got {totalEntries} entries"
        );

        var entry = Assert.Single(
            logger.Entries,
            candidate => candidate.Level == LogLevel.Information
        );
        Assert.Equal(200, entry.GetState<int>("OrdersDetected"));
        Assert.Equal(200, entry.GetState<int>("OrdersSucceeded"));

        // This fixture parses synthetic blocks against a one-byte document, so no slip can be
        // sliced for any of them. That is worth exactly one warning naming how many orders
        // went without — never one per order (017-pick-completion-handoff).
        var slipWarning = Assert.Single(
            logger.Entries,
            candidate => candidate.Level == LogLevel.Warning
        );
        Assert.Equal(200, slipWarning.GetState<int>("WithoutPackingSlipCount"));
    }

    private static void AssertNoLeakedContent(ImportTestSupport.LogEntry entry, string marker)
    {
        Assert.DoesNotContain(marker, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            entry.State,
            pair => pair.Value is string text && text.Contains(marker, StringComparison.Ordinal)
        );
    }

    /// <summary>A synthetic feed: listing costs two calls, each fetch batch three.</summary>
    private sealed class SyntheticOrderFeed(IReadOnlyList<string> openOrders) : ITcgplayerOrderFeed
    {
        public const string ProductName = "Synthetic Logging Card";

        public long CallCount { get; private set; } = 4_200;

        public int PageSize => 50;

        public bool Unreachable { get; init; }

        /// <summary>Order numbers whose candidate has no lines, so the validator rejects them.</summary>
        public IReadOnlyList<string> NoLineOrders { get; init; } = [];

        public Task<IReadOnlyList<string>> GetOpenOrderNumbersAsync(
            CancellationToken cancellationToken
        )
        {
            CallCount += 2;
            return Unreachable
                ? Task.FromException<IReadOnlyList<string>>(
                    new TcgplayerFeedException(TcgplayerFeedFailure.Unavailable, "timed out")
                )
                : Task.FromResult(openOrders);
        }

        public Task<IReadOnlyList<OrderCandidate>> GetOrdersAsync(
            IReadOnlyList<string> orderNumbers,
            CancellationToken cancellationToken
        )
        {
            CallCount += 3;
            IReadOnlyList<OrderCandidate> candidates = orderNumbers
                .Select(number => new OrderCandidate(
                    number,
                    NoLineOrders.Contains(number)
                        ? []
                        :
                        [
                            new OrderLineCandidate(
                                RawDescription: ProductName,
                                ProductLine: "Magic",
                                ProductName: ProductName,
                                Set: "Synthetic Set",
                                CollectorNumber: null,
                                Rarity: null,
                                Condition: "Near Mint",
                                Variant: null,
                                Language: "English",
                                ImageUrl: null,
                                Quantity: 1
                            ),
                        ]
                ))
                .ToList();
            return Task.FromResult(candidates);
        }
    }

    private sealed class CancellableParser : IPackingSlipParser
    {
        public async IAsyncEnumerable<PackingSlipParseUpdate> ParseAsync(
            Stream packingSlipPdf,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            for (var index = 1; index <= 3; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return new PackingSlipParseUpdate(index, false, null);
            }
        }
    }

    private sealed class SyntheticProgressiveParser(int orderCount) : IPackingSlipParser
    {
        public async IAsyncEnumerable<PackingSlipParseUpdate> ParseAsync(
            Stream packingSlipPdf,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            var blocks = new List<RawOrderBlock>();
            for (var index = 1; index <= orderCount; index++)
            {
                blocks.Add(
                    new RawOrderBlock
                    {
                        OrderIdentifier = $"SYNTHETIC-LOG-{index:D6}-ORDER",
                        PageNumbers = [1],
                        ProductLines =
                        [
                            new RawProductLine
                            {
                                QuantityText = "1",
                                RawDescription =
                                    "Pokemon - Test Set: Test Card - #1 - Common - Near Mint",
                            },
                        ],
                    }
                );
                await Task.Yield();
                yield return new PackingSlipParseUpdate(index, false, null);
            }

            yield return new PackingSlipParseUpdate(
                orderCount,
                true,
                new ParsedPackingSlip
                {
                    OrderBlocks = blocks,
                    SummaryPageFound = false,
                    SummaryOrderIdentifiers = [],
                }
            );
        }
    }

    private sealed class FailOrderLineInsertInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                eventData.CommandSource == CommandSource.SaveChanges
                && command.CommandText.Contains("[OrderLines]", StringComparison.Ordinal)
            )
            {
                throw new DbUpdateException("Injected order-line persistence failure.");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
