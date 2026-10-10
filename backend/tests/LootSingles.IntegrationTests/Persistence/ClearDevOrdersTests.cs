using System.Diagnostics;
using LootSingles.Application.Import;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Persistence;

/// <summary>
/// T071: the dev-only order reset. These run scripts/dev/clear-dev-orders.sql, and
/// scripts/dev/Clear-DevOrders.ps1 itself, only against Testcontainers databases.
/// </summary>
[Collection(SqlServerTestCollection.Name)]
public sealed class ClearDevOrdersTests(SqlServerContainerFixture fixture)
{
    private static readonly string[] OrderTables =
    [
        "PickingIssues",
        "PackingSlipAccesses",
        "OrderPackingSlips",
        "OrderLines",
        "ImportOrderResults",
        "ImportAttempts",
        "Orders",
    ];

    [Fact]
    public async Task Sql_deletes_every_order_table_and_keeps_employees_audit_events_and_keys()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await SeedOrderOfEachKindAsync(lease);

        var deleted = await RunClearSqlAsync(lease.ConnectionString, preview: false);

        Assert.Equal(OrderTables, deleted.Keys);
        Assert.Equal(1, deleted["PickingIssues"]);
        Assert.Equal(1, deleted["PackingSlipAccesses"]);
        Assert.Equal(1, deleted["OrderPackingSlips"]);
        Assert.Equal(5, deleted["OrderLines"]);
        Assert.Equal(2, deleted["ImportOrderResults"]);
        Assert.Equal(2, deleted["ImportAttempts"]);
        Assert.Equal(5, deleted["Orders"]);

        await using var context = lease.CreateDbContext();
        Assert.Equal(0, await context.Orders.CountAsync());
        Assert.Equal(0, await context.OrderLines.CountAsync());
        Assert.Equal(0, await context.PickingIssues.CountAsync());
        Assert.Equal(0, await context.OrderPackingSlips.CountAsync());
        Assert.Equal(0, await context.PackingSlipAccesses.CountAsync());
        Assert.Equal(0, await context.ImportAttempts.CountAsync());
        Assert.Equal(0, await context.ImportOrderResults.CountAsync());
        Assert.Equal(2, await context.Employees.CountAsync());
        Assert.Equal(1, await context.EmployeeAuditEvents.CountAsync());
        Assert.Equal(1, await context.DataProtectionKeys.CountAsync());
    }

    [Fact]
    public async Task Sql_preview_reports_the_counts_and_deletes_nothing()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await SeedOrderOfEachKindAsync(lease);

        var counts = await RunClearSqlAsync(lease.ConnectionString, preview: true);

        Assert.Equal(5, counts["Orders"]);
        Assert.Equal(1, counts["PickingIssues"]);
        await using var context = lease.CreateDbContext();
        Assert.Equal(5, await context.Orders.CountAsync());
        Assert.Equal(5, await context.OrderLines.CountAsync());
        Assert.Equal(1, await context.PickingIssues.CountAsync());
        Assert.Equal(2, await context.ImportOrderResults.CountAsync());
        Assert.Equal(
            1,
            await context.OrderLines.CountAsync(line => line.CurrentPickingIssueId != null)
        );
    }

    [Fact]
    public async Task Script_with_force_clears_orders_from_a_dev_named_database()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync(
            $"loot_singles_dev_t071_{Guid.NewGuid():N}",
            _ => Task.CompletedTask
        );
        await SeedOrderOfEachKindAsync(lease);

        var result = await RunScriptAsync("-ConnectionString", lease.ConnectionString, "-Force");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains(lease.DatabaseName, result.Output);
        Assert.DoesNotContain("Password", result.Output, StringComparison.OrdinalIgnoreCase);
        await using var context = lease.CreateDbContext();
        Assert.Equal(0, await context.Orders.CountAsync());
        Assert.Equal(0, await context.ImportAttempts.CountAsync());
        Assert.Equal(2, await context.Employees.CountAsync());
    }

    [Fact]
    public async Task Runner_refuses_a_database_the_server_reports_as_not_dev()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await SeedOrderOfEachKindAsync(lease);
        var scripts = Path.Combine(FindRepositoryRoot(), "scripts", "dev");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (
            var argument in new[]
            {
                "run",
                "--file",
                Path.Combine(scripts, "ClearDevOrders.cs"),
                "--",
                "delete",
                Path.Combine(scripts, "clear-dev-orders.sql"),
            }
        )
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["LOOT_CLEAR_DEV_ORDERS_CONNECTION"] = lease.ConnectionString;

        var result = await RunProcessAsync(startInfo);

        Assert.True(result.ExitCode == 3, result.Output);
        Assert.Contains("Refusing", result.Output);
        await using var context = lease.CreateDbContext();
        Assert.Equal(5, await context.Orders.CountAsync());
        Assert.Equal(2, await context.ImportAttempts.CountAsync());
    }

    private static async Task SeedOrderOfEachKindAsync(SqlServerDatabaseLease lease)
    {
        await using var context = lease.CreateDbContext();
        var picker = CreateEmployee("synpicker", EmployeeRole.Picker);
        var manager = CreateEmployee("synmanager", EmployeeRole.ManagerAdmin);
        context.Employees.AddRange(picker, manager);
        context.DataProtectionKeys.Add(
            new DataProtectionKey { FriendlyName = "syn-key", Xml = "<key />" }
        );
        await context.SaveChangesAsync();

        context.EmployeeAuditEvents.Add(
            new EmployeeAuditEvent
            {
                ActorEmployeeId = manager.Id,
                TargetEmployeeId = picker.Id,
                ActionType = EmployeeAuditActionType.AccountCreated,
                OccurredAt = DateTimeOffset.UtcNow,
            }
        );

        var pdfOrder = CreateOrder("SYN-T071-PDF", OrderImportSource.PackingSlipPdf);
        pdfOrder.PackingSlip = new OrderPackingSlip
        {
            Content = [0x25, 0x50, 0x44, 0x46],
            StoredAt = DateTimeOffset.UtcNow,
        };
        var apiOrder = CreateOrder("SYN-T071-API", OrderImportSource.TcgplayerApi);
        var claimedOrder = CreateOrder("SYN-T071-CLAIMED", OrderImportSource.TcgplayerApi);
        claimedOrder.Status = OrderStatus.InProgress;
        claimedOrder.ClaimedByEmployeeId = picker.Id;
        claimedOrder.ClaimedAt = DateTimeOffset.UtcNow;
        var issueOrder = CreateOrder("SYN-T071-ISSUE", OrderImportSource.TcgplayerApi);
        issueOrder.Status = OrderStatus.NeedsAttention;
        var packedOrder = CreateOrder("SYN-T071-PACKED", OrderImportSource.PackingSlipPdf);
        packedOrder.Status = OrderStatus.Packed;
        packedOrder.PackedByEmployeeId = manager.Id;
        packedOrder.PackedAt = DateTimeOffset.UtcNow;
        packedOrder.OrderLines.Single().PickOutcome = PickOutcome.Picked;
        packedOrder.OrderLines.Single().PickOutcomeRecordedByEmployeeId = manager.Id;
        packedOrder.OrderLines.Single().PickOutcomeRecordedAt = DateTimeOffset.UtcNow;
        context.Orders.AddRange(pdfOrder, apiOrder, claimedOrder, issueOrder, packedOrder);
        await context.SaveChangesAsync();

        context.PackingSlipAccesses.Add(
            new PackingSlipAccess
            {
                OrderId = pdfOrder.Id,
                EmployeeId = manager.Id,
                RetrievedAt = DateTimeOffset.UtcNow,
            }
        );
        var issueLine = issueOrder.OrderLines.Single();
        var issue = new PickingIssue
        {
            OrderLineId = issueLine.Id,
            IssueType = PickingIssueType.CardNotFound,
            ReportedByEmployeeId = picker.Id,
            ReportedAt = DateTimeOffset.UtcNow,
        };
        context.PickingIssues.Add(issue);
        var pdfAttempt = new ImportAttempt
        {
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Source = OrderImportSource.PackingSlipPdf,
        };
        var apiAttempt = new ImportAttempt
        {
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Source = OrderImportSource.TcgplayerApi,
        };
        context.ImportAttempts.AddRange(pdfAttempt, apiAttempt);
        await context.SaveChangesAsync();

        issueLine.PickOutcome = PickOutcome.HasIssue;
        issueLine.PickOutcomeRecordedByEmployeeId = picker.Id;
        issueLine.PickOutcomeRecordedAt = DateTimeOffset.UtcNow;
        issueLine.CurrentPickingIssueId = issue.Id;
        context.ImportOrderResults.AddRange(
            new ImportOrderResult
            {
                ImportAttemptId = apiAttempt.Id,
                SourceOrderIdentifier = apiOrder.TcgplayerOrderId,
                Outcome = ImportOutcome.Succeeded,
                ResultingOrderId = apiOrder.Id,
            },
            new ImportOrderResult
            {
                ImportAttemptId = pdfAttempt.Id,
                SourceOrderIdentifier = "SYN-T071-REJECTED",
                Outcome = ImportOutcome.Rejected,
                FailureCode = FailureType.MissingSet,
                FailureMessage = "synthetic",
            }
        );
        await context.SaveChangesAsync();
    }

    private static async Task<Dictionary<string, int>> RunClearSqlAsync(
        string connectionString,
        bool preview
    )
    {
        var sql = await File.ReadAllTextAsync(
            Path.Combine(FindRepositoryRoot(), "scripts", "dev", "clear-dev-orders.sql")
        );
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(
            new SqlParameter("@Preview", System.Data.SqlDbType.Bit) { Value = preview }
        );
        var counts = new Dictionary<string, int>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            counts.Add(reader.GetString(0), reader.GetInt32(1));
        }

        return counts;
    }

    internal static async Task<(int ExitCode, string Output)> RunScriptAsync(
        params string[] scriptArguments
    )
    {
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach (
            var argument in new[]
            {
                "-NoProfile",
                "-NonInteractive",
                "-File",
                Path.Combine(FindRepositoryRoot(), "scripts", "dev", "Clear-DevOrders.ps1"),
            }.Concat(scriptArguments)
        )
        {
            startInfo.ArgumentList.Add(argument);
        }

        return await RunProcessAsync(startInfo);
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        ProcessStartInfo startInfo
    )
    {
        startInfo.RedirectStandardInput = true;
        using var process = Process.Start(startInfo)!;
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await stdout + await stderr);
    }

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static Employee CreateEmployee(string username, EmployeeRole role) =>
        new()
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = username,
            PinHash = "synthetic-hash-not-a-pin",
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static Order CreateOrder(string identifier, OrderImportSource source) =>
        new()
        {
            TcgplayerOrderId = identifier,
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow,
            ImportSource = source,
            OrderLines =
            [
                new OrderLine
                {
                    RawDescription = "raw",
                    ProductLine = "Magic",
                    ProductName = "Synthetic Card",
                    Set = "Synthetic Set",
                    CollectorNumber = "1",
                    Condition = "Near Mint",
                    Quantity = 1,
                },
            ],
        };
}

/// <summary>
/// T071: the script's dev-database guard. Runs without a database: every connection string here
/// points at a host that cannot resolve, so a guard that failed open would error on connecting
/// rather than print the refusal.
/// </summary>
public sealed class ClearDevOrdersGuardTests
{
    [Theory]
    [InlineData("Server=tcp:loot-stage.example.invalid,1433;Database=loot-singles-stage")]
    [InlineData("Data Source=loot-prod.example.invalid;Initial Catalog=LootSingles")]
    [InlineData(
        "Server=tcp:loot.example.invalid;Database=loot-singles;Authentication=Active Directory Default"
    )]
    [InlineData("Server=tcp:loot.example.invalid;Database=old-dev;Initial Catalog=lootsingles")]
    [InlineData("Server=tcp:loot.example.invalid;Initial Catalog=old-dev;Database=lootsingles")]
    [InlineData("Server=tcp:loot.example.invalid;Database=devices")]
    public async Task Script_refuses_a_database_without_dev_in_its_name_even_with_force(
        string connectionString
    )
    {
        var result = await ClearDevOrdersTests.RunScriptAsync(
            "-ConnectionString",
            connectionString,
            "-Force"
        );

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Refusing", result.Output);
        Assert.DoesNotContain("example.invalid;", result.Output);
        Assert.DoesNotContain("Authentication=", result.Output);
        Assert.DoesNotContain("Initial Catalog=", result.Output);
    }

    [Theory]
    [InlineData("loot-singles.example.invalid", "loot-singles-dev", true)]
    [InlineData("loot-singles.example.invalid", "LootSingles.DEV", true)]
    [InlineData("loot-singles.example.invalid", "LootSinglesFulfillment.Dev", true)]
    [InlineData("loot-singles.example.invalid", "loot_singles_dev_t071_0123abcd", true)]
    [InlineData("loot-singles.example.invalid", "dev", true)]
    [InlineData("loot-singles.example.invalid", "devices", false)]
    [InlineData("loot-singles.example.invalid", "ondevelopment", false)]
    [InlineData("loot-singles.example.invalid", "loot-singles-devstage", false)]
    [InlineData("(localdb)\\MSSQLLocalDB", "LootSingles", true)]
    [InlineData("(LocalDB)\\MSSQLLocalDB", "LootSingles", true)]
    [InlineData("loot-singles.example.invalid", "loot-singles-stage", false)]
    [InlineData("loot-dev.example.invalid", "loot-singles", false)]
    [InlineData("loot-singles.example.invalid", "", false)]
    public async Task Guard_allows_only_a_dev_database_or_localdb(
        string server,
        string database,
        bool expected
    )
    {
        var scriptPath = Path.Combine(
            ClearDevOrdersTests.FindRepositoryRoot(),
            "scripts",
            "dev",
            "Clear-DevOrders.ps1"
        );
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            $". {Quote(scriptPath)}; Test-DevDatabase -Server {Quote(server)} -Database {Quote(database)}"
        );

        using var process = Process.Start(startInfo)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, stdout + stderr);
        Assert.Equal(expected ? "True" : "False", stdout.Trim());
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
