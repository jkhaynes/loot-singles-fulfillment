using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace LootSingles.IntegrationTests.Infrastructure;

public sealed class E2EHostDatabaseTests
{
    [Fact]
    public async Task Host_reports_sql_server_health_and_persisted_seed_data()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var process = Process.Start(
            new ProcessStartInfo("dotnet")
            {
                ArgumentList =
                {
                    "run",
                    "--project",
                    Path.Combine(repositoryRoot, "backend", "tests", "LootSingles.E2EHost"),
                    "--no-build",
                },
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }
        )!;

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5098") };
            var health = await WaitForHealthAsync(client, process);
            Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", health.DatabaseProvider);
            Assert.True(health.Seeded);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private static async Task<HealthResponse> WaitForHealthAsync(HttpClient client, Process process)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        while (!timeout.IsCancellationRequested && !process.HasExited)
        {
            try
            {
                var response = await client.GetFromJsonAsync<HealthResponse>(
                    "/health",
                    timeout.Token
                );
                if (response is not null)
                {
                    return response;
                }
            }
            catch (HttpRequestException)
            {
                await Task.Delay(250, timeout.Token);
            }
            catch (JsonException)
            {
                throw new Xunit.Sdk.XunitException(
                    "E2E /health did not return the database health contract."
                );
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"E2E host did not become healthy. Exit code: {(process.HasExited ? process.ExitCode : null)}"
        );
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed record HealthResponse(string DatabaseProvider, bool Seeded);
}
