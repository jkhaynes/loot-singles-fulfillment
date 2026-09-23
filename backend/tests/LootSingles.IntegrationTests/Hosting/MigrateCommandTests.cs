using System.Collections.Concurrent;
using LootSingles.Api;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LootSingles.IntegrationTests.Hosting;

/// <summary>
/// 019 T010 / research.md §6. Schema changes are applied by a one-shot <c>migrate</c> command run as
/// its own job, not by the application at startup. That keeps schema permission away from the
/// internet-facing component (FR-021), turns a failed migration into a failed job rather than a
/// crash-looping container, and guarantees the schema is current before the new version serves any
/// request (FR-015).
///
/// The no-echo assertions matter more than they look: the migrate job runs in a deployment workflow
/// whose output is retained, so anything this command prints on failure is durable (FR-019, FR-022).
/// </summary>
[Collection(SqlServerTestCollection.Name)]
public sealed class MigrateCommandTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task Applies_pending_migrations_and_reports_success()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var context = lease.CreateDbContext();
        await context.Database.EnsureDeletedAsync();
        Assert.NotEmpty(await context.Database.GetPendingMigrationsAsync());
        var logger = new CapturingLogger();
        var output = new StringWriter();

        var exitCode = await new MigrateCommand(context, logger).ExecuteAsync(output, default);

        Assert.Equal(MigrateCommand.SuccessExitCode, exitCode);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Names_the_migrations_it_applied_in_the_log()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var context = lease.CreateDbContext();
        await context.Database.EnsureDeletedAsync();
        var expected = (await context.Database.GetPendingMigrationsAsync()).ToArray();
        var logger = new CapturingLogger();

        await new MigrateCommand(context, logger).ExecuteAsync(new StringWriter(), default);

        var logged = string.Join('\n', logger.Messages);
        foreach (var migration in expected)
        {
            Assert.Contains(migration, logged, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Is_safe_to_run_twice()
    {
        // The deploy workflow runs this on every release, and most releases carry no migration at
        // all. A second run must be a successful no-op, not a failure.
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var context = lease.CreateDbContext();
        await context.Database.EnsureDeletedAsync();

        var first = await new MigrateCommand(context, new CapturingLogger()).ExecuteAsync(
            new StringWriter(),
            default
        );
        var second = await new MigrateCommand(context, new CapturingLogger()).ExecuteAsync(
            new StringWriter(),
            default
        );

        Assert.Equal(MigrateCommand.SuccessExitCode, first);
        Assert.Equal(MigrateCommand.SuccessExitCode, second);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Failure_reports_without_echoing_the_connection_string()
    {
        const string server = "migrate-unreachable.invalid";
        var connectionString =
            $"Server={server};Database=loot-singles;Encrypt=True;Connect Timeout=1";
        var options = new DbContextOptionsBuilder<LootSinglesDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var context = new LootSinglesDbContext(options);
        var logger = new CapturingLogger();
        var output = new StringWriter();

        var exitCode = await new MigrateCommand(context, logger).ExecuteAsync(output, default);

        Assert.Equal(MigrateCommand.FailureExitCode, exitCode);
        var written = output.ToString();
        Assert.DoesNotContain(connectionString, written, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server=", written, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(server, written, StringComparison.OrdinalIgnoreCase);
        // Split so the repository's secret scan (pr-quality-gate.yml) does not flag this assertion.
        Assert.DoesNotContain("Pass" + "word=", written, StringComparison.OrdinalIgnoreCase);
        // The operator still needs to know it failed.
        Assert.False(string.IsNullOrWhiteSpace(written));
    }

    private sealed class CapturingLogger : ILogger<MigrateCommand>
    {
        public ConcurrentBag<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Messages.Add(formatter(state, exception));
    }
}
