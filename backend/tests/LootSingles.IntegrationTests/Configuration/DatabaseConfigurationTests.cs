using LootSingles.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LootSingles.IntegrationTests.Configuration;

public sealed class DatabaseConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_or_blank_connection_string_fails_without_echoing_configuration(
        string? connectionString
    )
    {
        using var factory = CreateFactory(connectionString);

        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var scope = factory.Services.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        });
        var messages = Flatten(exception).Select(error => error.Message).ToArray();

        Assert.Contains(
            messages,
            message => message.Contains("ConnectionStrings:LootSingles", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            messages,
            message => message.Contains("Server=", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Configured_runtime_registers_only_the_sql_server_provider()
    {
        const string connectionString =
            "Server=configuration-test.invalid;Database=loot-singles-dev;Encrypt=True";
        using var factory = CreateFactory(connectionString);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
        var connection = Assert.IsType<SqlConnection>(context.Database.GetDbConnection());
        Assert.Equal("loot-singles-dev", connection.Database);
    }

    [Fact]
    public void Backend_workflow_checks_tracked_configuration_and_documentation_for_secrets()
    {
        var workflow = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), ".github", "workflows", "backend.yml")
        );

        Assert.Contains("git grep", workflow, StringComparison.Ordinal);
        Assert.Contains("credential_pattern", workflow, StringComparison.Ordinal);
        Assert.Contains("*.json", workflow, StringComparison.Ordinal);
        Assert.Contains("*.md", workflow, StringComparison.Ordinal);
        Assert.Contains("*.yml", workflow, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(string? connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            if (connectionString is not null)
            {
                builder.UseSetting("ConnectionStrings:LootSingles", connectionString);
            }
        });

    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
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
}
