using LootSingles.Application.Import;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Import;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
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

    public static PackingSlipImportService CreateService(LootSinglesDbContext context)
    {
        var repository = new ImportRepository(context);
        return new(
            new PdfPigPackingSlipParser(),
            new PdfPigPackingSlipSlicer(),
            new OrderImporter(repository, NullLogger<OrderImporter>.Instance),
            repository,
            NullLogger<PackingSlipImportService>.Instance
        );
    }

    public static PackingSlipImportService CreateDatabaseFreeService(
        IPackingSlipParser? parser = null
    )
    {
        var persistence = new FakeImportPersistence();
        return new(
            parser ?? new PdfPigPackingSlipParser(),
            new PdfPigPackingSlipSlicer(),
            new OrderImporter(persistence, NullLogger<OrderImporter>.Instance),
            persistence,
            NullLogger<PackingSlipImportService>.Instance
        );
    }

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

    internal sealed class CapturingLogger<T> : ILogger<T>
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
                    exception,
                    (state as IReadOnlyList<KeyValuePair<string, object>>) ?? []
                )
            );
    }

    // Hands every category a CapturingLogger, so one list holds the whole host's log output.
    internal sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly Lock _gate = new();
        private readonly List<CapturingLogger<object>> _loggers = [];

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_gate)
                {
                    return _loggers.SelectMany(logger => logger.Entries.ToArray()).ToArray();
                }
            }
        }

        public ILogger CreateLogger(string categoryName)
        {
            var logger = new CapturingLogger<object>();
            lock (_gate)
            {
                _loggers.Add(logger);
            }

            return logger;
        }

        public void Dispose() { }
    }

    internal sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyList<KeyValuePair<string, object>> State
    );

    public static T GetState<T>(this LogEntry entry, string key) =>
        (T)entry.State.Single(pair => pair.Key == key).Value;

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
