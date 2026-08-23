using System.Data.Common;
using System.Runtime.CompilerServices;
using LootSingles.Application.Import;
using LootSingles.Infrastructure.Import;
using LootSingles.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

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
        var service = new PackingSlipImportService(
            new PdfPigPackingSlipParser(),
            new ImportRepository(context),
            logger
        );

        var final = await ImportTestSupport.ImportFixtureAsync(service, "corrupted-file.pdf");

        Assert.Equal(FailureType.UnreadablePdf, final.ImportAttempt.AttemptFailureCode);
        Assert.NotEqual(0, final.ImportAttempt.Id);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(final.ImportAttempt.Id, entry.GetState<int>("ImportId"));
        Assert.Equal(FailureType.UnreadablePdf, entry.GetState<FailureType>("AttemptFailureType"));
    }

    [Fact]
    public async Task ImportAsync_DuplicateOrderReimport_ProducesWarningWithDuplicateBreakdown()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext();
        var firstService = ImportTestSupport.CreateService(context);
        await ImportTestSupport.ImportFixtureAsync(firstService, "valid-multi-order-batch.pdf");

        var logger = new ImportTestSupport.CapturingLogger<PackingSlipImportService>();
        var service = new PackingSlipImportService(
            new PdfPigPackingSlipParser(),
            new ImportRepository(context),
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
        Assert.Equal(13, entry.GetState<int>("OrdersDetected"));
        Assert.Equal(0, entry.GetState<int>("OrdersSucceeded"));
        Assert.Equal(13, entry.GetState<int>("OrdersFailed"));
        Assert.Equal(13, entry.GetState<int>("DuplicateOrderCount"));
        AssertNoLeakedContent(entry, ProductMarker);
    }

    [Fact]
    public async Task ImportAsync_OrderPersistenceFailure_ProducesErrorWithImportIdOrderIdAndFailureType()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext(
            new FailOrderLineInsertInterceptor()
        );
        var logger = new ImportTestSupport.CapturingLogger<PackingSlipImportService>();
        var service = new PackingSlipImportService(
            new PdfPigPackingSlipParser(),
            new ImportRepository(context),
            logger
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
        var service = new PackingSlipImportService(
            new PdfPigPackingSlipParser(),
            new ImportRepository(context),
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
        Assert.Equal(13, entry.GetState<int>("OrdersDetected"));
        Assert.Equal(13, entry.GetState<int>("OrdersSucceeded"));
        AssertNoLeakedContent(entry, ProductMarker);
    }

    [Fact]
    public async Task ImportAsync_CancelledMidBatch_ProducesNoWarningOrErrorLogEntry()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext();
        var logger = new ImportTestSupport.CapturingLogger<PackingSlipImportService>();
        var service = new PackingSlipImportService(
            new CancellableParser(),
            new ImportRepository(context),
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
            logger.Entries,
            entry => entry.Level is LogLevel.Warning or LogLevel.Error
        );
    }

    private static void AssertNoLeakedContent(ImportTestSupport.LogEntry entry, string marker)
    {
        Assert.DoesNotContain(marker, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            entry.State,
            pair => pair.Value is string text && text.Contains(marker, StringComparison.Ordinal)
        );
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
