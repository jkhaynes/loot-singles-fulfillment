using System.Runtime.CompilerServices;
using LootSingles.Application.Import;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Auth;
using LootSingles.IntegrationTests.ImportUi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace LootSingles.IntegrationTests.Import;

public sealed class ParserExceptionSafetyTests
{
    private const string SafeMessage =
        "The supplied PDF could not be read as a TCGplayer packing slip.";
    private const string Secret = "secret-customer-pdf-content";

    [Fact]
    public async Task ParserExceptionPersistsSafeMessageAndLogsOnlySafeClassification()
    {
        await using var context = ImportTestSupport.CreateDatabaseContext();
        var logger = new CapturingLogger<PackingSlipImportService>();
        var service = new PackingSlipImportService(
            new ThrowingParser(),
            new ImportRepository(context),
            logger
        );

        ImportProgressUpdate? final = null;
        await foreach (var update in service.ImportAsync(Stream.Null))
        {
            final = update;
        }

        Assert.NotNull(final);
        Assert.Equal(FailureType.UnreadablePdf, final.ImportAttempt.AttemptFailureCode);
        Assert.Equal(SafeMessage, final.ImportAttempt.AttemptFailureMessage);
        Assert.DoesNotContain(Secret, final.ImportAttempt.AttemptFailureMessage);

        var persisted = await context.ImportAttempts.SingleAsync();
        Assert.Equal(SafeMessage, persisted.AttemptFailureMessage);
        Assert.DoesNotContain(Secret, persisted.AttemptFailureMessage);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains(nameof(InvalidDataException), entry.Message);
        Assert.DoesNotContain(Secret, entry.Message);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task ParserExceptionHttpResponseAndPersistedAttemptDoNotExposeSecret()
    {
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPackingSlipParser>();
                services.AddScoped<IPackingSlipParser, ThrowingParser>();
            })
        );
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        using var form = ImportUiTestSupport.FileForm([1]);

        var response = await client.PostAsync("/api/imports", form);
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(Secret, body);
        Assert.Contains(SafeMessage, body);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        var persisted = await context.ImportAttempts.SingleAsync();
        Assert.Equal(SafeMessage, persisted.AttemptFailureMessage);
        Assert.DoesNotContain(Secret, persisted.AttemptFailureMessage);
    }

    private sealed class ThrowingParser : IPackingSlipParser
    {
        public async IAsyncEnumerable<PackingSlipParseUpdate> ParseAsync(
            Stream packingSlipPdf,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.Yield();
            throw new InvalidDataException(Secret);
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
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
        ) => Entries.Add(new LogEntry(formatter(state, exception), exception));
    }

    private sealed record LogEntry(string Message, Exception? Exception);
}
