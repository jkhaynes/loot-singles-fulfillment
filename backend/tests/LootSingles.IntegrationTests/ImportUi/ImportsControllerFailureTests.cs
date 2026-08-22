using System.Runtime.CompilerServices;
using System.Text.Json;
using LootSingles.Application.Import;
using LootSingles.IntegrationTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LootSingles.IntegrationTests.ImportUi;

public sealed class ImportsControllerFailureTests
{
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
}
