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
}
