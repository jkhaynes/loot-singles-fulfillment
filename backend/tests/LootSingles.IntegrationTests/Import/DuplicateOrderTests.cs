using LootSingles.Application.Import;
using LootSingles.Infrastructure.Import;
using LootSingles.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Import;

public class DuplicateOrderTests
{
    [Fact]
    public async Task ImportAsync_SameFixtureTwice_RejectsDuplicatesWithoutChangingOriginalOrders()
    {
        await using var context = ImportTestSupport.CreateInMemoryContext();
        var service = ImportTestSupport.CreateService(context);
        await ImportTestSupport.ImportFixtureAsync(service, "valid-multi-order-batch.pdf");
        var originalLines = await context.OrderLines
            .AsNoTracking()
            .OrderBy(line => line.Id)
            .Select(line => new { line.Id, line.OrderId, line.RawDescription, line.Quantity })
            .ToListAsync();

        var second = await ImportTestSupport.ImportFixtureAsync(service, "valid-multi-order-batch.pdf");

        Assert.All(second.ImportAttempt.ImportOrderResults, result =>
        {
            Assert.Equal(ImportOutcome.Rejected, result.Outcome);
            Assert.Equal(FailureType.DuplicateOrder, result.FailureCode);
        });
        Assert.Equal(13, await context.Orders.CountAsync());
        Assert.Equal(originalLines, await context.OrderLines
            .AsNoTracking()
            .OrderBy(line => line.Id)
            .Select(line => new { line.Id, line.OrderId, line.RawDescription, line.Quantity })
            .ToListAsync());
    }

    [Fact]
    public async Task ImportAsync_ConcurrentSameOrder_DatabaseConstraintRejectsRaceLoser()
    {
        var databaseName = $"race-{Guid.NewGuid():N}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var setup = CreateSqliteContext(connectionString)) await setup.Database.EnsureCreatedAsync();

        var barrier = new Barrier(2);
        await using var firstContext = CreateSqliteContext(connectionString);
        await using var secondContext = CreateSqliteContext(connectionString);
        var firstService = new PackingSlipImportService(
            new PdfPigPackingSlipParser(), new CoordinatedPersistence(firstContext, barrier));
        var secondService = new PackingSlipImportService(
            new PdfPigPackingSlipParser(), new CoordinatedPersistence(secondContext, barrier));

        var results = await Task.WhenAll(
            ImportTestSupport.ImportFixtureAsync(firstService, "duplicate-product-line-same-order.pdf"),
            ImportTestSupport.ImportFixtureAsync(secondService, "duplicate-product-line-same-order.pdf"));

        await using var verification = CreateSqliteContext(connectionString);
        Assert.Equal(1, await verification.Orders.CountAsync());
        var allResults = results.SelectMany(result => result.ImportAttempt.ImportOrderResults);
        Assert.Single(allResults, result => result.Outcome == ImportOutcome.Succeeded);
        var loser = Assert.Single(allResults, result => result.Outcome == ImportOutcome.Rejected);
        Assert.Equal(FailureType.DuplicateOrder, loser.FailureCode);
    }

    private static LootSinglesDbContext CreateSqliteContext(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<LootSinglesDbContext>().UseSqlite(connectionString);
        return new LootSinglesDbContext(builder.Options);
    }

    private sealed class CoordinatedPersistence(LootSinglesDbContext inner, Barrier barrier) : IImportPersistence
    {
        private readonly ImportRepository _repository = new(inner);

        public void AddImportAttempt(ImportAttempt attempt) => _repository.AddImportAttempt(attempt);

        public void AddOrder(LootSingles.Domain.Orders.Order order) => _repository.AddOrder(order);

        public void DiscardOrder(LootSingles.Domain.Orders.Order order) => _repository.DiscardOrder(order);

        public async Task<bool> OrderExistsAsync(string id, CancellationToken cancellationToken)
        {
            var exists = await _repository.OrderExistsAsync(id, cancellationToken);
            await Task.Run(() => barrier.SignalAndWait(cancellationToken), cancellationToken);
            return exists;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            _repository.SaveChangesAsync(cancellationToken);
    }
}
