using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LootSingles.Api.Controllers;
using LootSingles.Application.Import;
using LootSingles.IntegrationTests.Auth;
using LootSingles.IntegrationTests.Import;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace LootSingles.IntegrationTests.ImportUi;

public sealed class ImportsControllerFailureTests
{
    [Fact]
    public async Task ExceptionBeforeProgressReturnsSafe500ProblemDetails()
    {
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPackingSlipImportService>();
                services.AddScoped<IPackingSlipImportService, EarlyThrowingService>();
            })
        );
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        using var form = ImportUiTestSupport.FileForm([1]);

        var response = await client.PostAsync("/api/imports", form);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(
            "secret customer detail",
            content,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain("\"status\":\"failed\"", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExceptionAfterProgressEndsWithSafeFailedSnapshot()
    {
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPackingSlipImportService>();
                services.AddScoped<IPackingSlipImportService, ThrowingService>();
            })
        );
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        using var form = ImportUiTestSupport.FileForm([1]);

        var response = await client.PostAsync("/api/imports", form);
        var content = await response.Content.ReadAsStringAsync();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using var terminalDocument = JsonDocument.Parse(lines[^1]);
        var terminal = terminalDocument.RootElement;

        Assert.Equal("failed", terminal.GetProperty("status").GetString());
        Assert.DoesNotContain(
            "secret",
            terminal.GetProperty("operationFailureMessage").GetString()
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnexpectedExceptionLogsOneErrorWithTheTypeOnly(bool afterProgress)
    {
        var logger = new ImportTestSupport.CapturingLogger<ImportsController>();
        await using var rootFactory = new AuthWebApplicationFactory();
        await using var factory = rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPackingSlipImportService>();
                if (afterProgress)
                {
                    services.AddScoped<IPackingSlipImportService, ThrowingService>();
                }
                else
                {
                    services.AddScoped<IPackingSlipImportService, EarlyThrowingService>();
                }
                services.AddSingleton<ILogger<ImportsController>>(logger);
            })
        );
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        using var form = ImportUiTestSupport.FileForm([1]);

        var response = await client.PostAsync("/api/imports", form);
        await response.Content.ReadAsStringAsync();

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(nameof(InvalidOperationException), entry.GetState<string>("ExceptionType"));
        // The message could embed response text, so neither it nor the exception is logged.
        Assert.Null(entry.Exception);
        Assert.DoesNotContain("secret", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            entry.State,
            pair => pair.Value?.ToString()?.Contains("secret", StringComparison.Ordinal) == true
        );
    }

    private sealed class ThrowingService : IPackingSlipImportService
    {
        public async IAsyncEnumerable<ImportProgressUpdate> ImportAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken token = default
        )
        {
            var attempt = new ImportAttempt { StartedAt = DateTimeOffset.UtcNow };

            yield return new ImportProgressUpdate(
                OrdersDetected: 2,
                OrdersProcessed: 1,
                SucceededCount: 1,
                FailedCount: 0,
                IsComplete: false,
                attempt
            );

            await Task.Yield();
            throw new InvalidOperationException("secret customer detail");
        }
    }

    private sealed class EarlyThrowingService : IPackingSlipImportService
    {
        public async IAsyncEnumerable<ImportProgressUpdate> ImportAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken token = default
        )
        {
            await Task.Yield();
            if (stream.CanRead)
            {
                throw new InvalidOperationException("secret customer detail");
            }

            yield break;
        }
    }
}
