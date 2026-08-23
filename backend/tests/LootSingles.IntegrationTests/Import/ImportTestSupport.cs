using LootSingles.Application.Import;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Import;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace LootSingles.IntegrationTests.Import;

internal static class ImportTestSupport
{
    public static LootSinglesDbContext CreateDatabaseContext(params IInterceptor[] interceptors)
    {
        var lease = SqlServerContainerFixture
            .CreateSharedDatabaseLeaseAsync()
            .GetAwaiter()
            .GetResult();
        var options = new DbContextOptionsBuilder<LootSinglesDbContext>().UseSqlServer(
            lease.ConnectionString
        );
        if (interceptors.Length > 0)
        {
            options.AddInterceptors(interceptors);
        }

        return new LeasedDbContext(options.Options, lease);
    }

    public static PackingSlipImportService CreateService(LootSinglesDbContext context) =>
        new(
            new PdfPigPackingSlipParser(),
            new ImportRepository(context),
            NullLogger<PackingSlipImportService>.Instance
        );

    public static PackingSlipImportService CreateDatabaseFreeService(
        IPackingSlipParser? parser = null
    ) =>
        new(
            parser ?? new PdfPigPackingSlipParser(),
            new FakeImportPersistence(),
            NullLogger<PackingSlipImportService>.Instance
        );

    public static FileStream OpenFixture(string name) =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PackingSlips", name));

    public static async Task<ImportProgressUpdate> ImportFixtureAsync(
        IPackingSlipImportService service,
        string name
    )
    {
        await using var stream = OpenFixture(name);
        ImportProgressUpdate? final = null;
        await foreach (var update in service.ImportAsync(stream))
            final = update;
        return Assert.IsType<ImportProgressUpdate>(final);
    }

    private sealed class LeasedDbContext(
        DbContextOptions<LootSinglesDbContext> options,
        SqlServerDatabaseLease lease
    ) : LootSinglesDbContext(options)
    {
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await lease.DisposeAsync();
        }
    }

    private sealed class FakeImportPersistence : IImportPersistence
    {
        private readonly HashSet<string> _persistedOrderIds = new(StringComparer.Ordinal);
        private readonly HashSet<Order> _pendingOrders = [];

        public void AddImportAttempt(ImportAttempt attempt) { }

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
            }

            _pendingOrders.Clear();
            return Task.CompletedTask;
        }
    }
}
