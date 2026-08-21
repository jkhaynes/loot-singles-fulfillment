using System.Data.Common;
using LootSingles.Application.Import;
using LootSingles.Infrastructure.Import;
using LootSingles.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LootSingles.IntegrationTests.Import;

public class AtomicPersistenceTests
{
    [Fact]
    public async Task ImportAsync_LineInsertFails_RollsBackEntireOrderAndRecordsRejection()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LootSinglesDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new FailOrderLineInsertInterceptor())
            .Options;
        await using var context = new LootSinglesDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var service = new PackingSlipImportService(new PdfPigPackingSlipParser(), context);

        var final = await ImportTestSupport.ImportFixtureAsync(
            service, "duplicate-product-line-same-order.pdf");

        Assert.Empty(await context.Orders.AsNoTracking().ToListAsync());
        Assert.Empty(await context.OrderLines.AsNoTracking().ToListAsync());
        var result = Assert.Single(final.ImportAttempt.ImportOrderResults);
        Assert.Equal(ImportOutcome.Rejected, result.Outcome);
        Assert.Equal(FailureType.PersistenceFailure, result.FailureCode);
        Assert.Contains("DUPLICATE-LINE-FIXTURE", result.FailureMessage);
    }

    [Fact]
    public async Task ImportAsync_NonDuplicateFailureWhoseMessageSaysUnique_ReportsPersistenceFailure()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LootSinglesDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new MisleadingUniqueMessageInterceptor())
            .Options;
        await using var context = new LootSinglesDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var service = new PackingSlipImportService(new PdfPigPackingSlipParser(), context);

        var final = await ImportTestSupport.ImportFixtureAsync(
            service, "duplicate-product-line-same-order.pdf");

        var result = Assert.Single(final.ImportAttempt.ImportOrderResults);
        Assert.Equal(ImportOutcome.Rejected, result.Outcome);
        Assert.Equal(FailureType.PersistenceFailure, result.FailureCode);
    }

    [Fact]
    public async Task ImportAsync_PersistenceFailureOnOneOrder_StillPersistsSiblingOrdersInSameBatch()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LootSinglesDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new FailSpecificOrderLineInsertInterceptor("Orim's Chant"))
            .Options;
        await using var context = new LootSinglesDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var service = new PackingSlipImportService(new PdfPigPackingSlipParser(), context);

        var final = await ImportTestSupport.ImportFixtureAsync(service, "valid-multi-order-batch.pdf");

        Assert.Equal(13, final.OrdersProcessed);
        Assert.Equal(12, final.SucceededCount);
        Assert.Equal(1, final.FailedCount);
        Assert.Equal(12, await context.Orders.AsNoTracking().CountAsync());
        var failedResult = Assert.Single(
            final.ImportAttempt.ImportOrderResults,
            result => result.Outcome == ImportOutcome.Rejected);
        Assert.Equal(FailureType.PersistenceFailure, failedResult.FailureCode);
        Assert.Equal("F0000010-ABC010-00010", failedResult.SourceOrderIdentifier);
        Assert.DoesNotContain(
            await context.Orders.AsNoTracking().ToListAsync(),
            order => order.TcgplayerOrderId == "F0000010-ABC010-00010");
    }

    private sealed class FailOrderLineInsertInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"OrderLines\"", StringComparison.Ordinal))
            {
                throw new DbUpdateException("Injected order-line persistence failure.");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class MisleadingUniqueMessageInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"OrderLines\"", StringComparison.Ordinal))
            {
                throw new DbUpdateException("UNIQUE appears in this message, but this is not a duplicate-key error.");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class FailSpecificOrderLineInsertInterceptor(string marker) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"OrderLines\"", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(parameter =>
                    parameter.Value is string text && text.Contains(marker, StringComparison.Ordinal)))
            {
                throw new DbUpdateException($"Injected persistence failure for the order matching '{marker}'.");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
