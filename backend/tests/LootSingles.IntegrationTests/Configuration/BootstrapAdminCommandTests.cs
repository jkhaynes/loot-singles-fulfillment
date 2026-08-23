using System.Diagnostics;
using LootSingles.Api;
using LootSingles.Application.Auth;
using LootSingles.Domain.Employees;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LootSingles.IntegrationTests.Configuration;

[Collection(SqlServerTestCollection.Name)]
public sealed class BootstrapAdminCommandTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task CliProcess_WithTemporaryEnvironment_BootstrapsAndExitsWithoutHosting()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        const string pin = "6284";
        var startInfo = new ProcessStartInfo(
            "dotnet",
            $"\"{typeof(Program).Assembly.Location}\" bootstrap-admin"
        )
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["ConnectionStrings__LootSingles"] = lease.ConnectionString;
        startInfo.Environment["BootstrapAdmin__Username"] = "processmanager";
        startInfo.Environment["BootstrapAdmin__DisplayName"] = "Process Manager";
        startInfo.Environment["BootstrapAdmin__Pin"] = pin;

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start bootstrap process.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The bootstrap-admin process did not exit within the 60-second timeout.");
        }

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        Assert.Equal(BootstrapAdminCommand.SuccessExitCode, process.ExitCode);
        Assert.Contains(
            "Initial Manager/Admin employee created.",
            standardOutput,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(pin, standardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(pin, standardError, StringComparison.Ordinal);
        Assert.DoesNotContain(lease.ConnectionString, standardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(lease.ConnectionString, standardError, StringComparison.Ordinal);
        await using var verification = lease.CreateDbContext();
        var employee = Assert.Single(await verification.Employees.AsNoTracking().ToListAsync());
        Assert.Equal("PROCESSMANAGER", employee.NormalizedUsername);
    }

    [Fact]
    public async Task ExecuteAsync_AppliesMigrationsAndPersistsFirstManagerWithoutSecretsInOutput()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var context = lease.CreateDbContext();
        await context.Database.EnsureDeletedAsync();
        const string pin = "7391";
        var output = new StringWriter();
        var command = CreateCommand(context, pin);

        var exitCode = await command.ExecuteAsync(output, default);

        Assert.Equal(BootstrapAdminCommand.SuccessExitCode, exitCode);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        var employee = Assert.Single(await context.Employees.AsNoTracking().ToListAsync());
        Assert.Equal("BOOTSTRAPMANAGER", employee.NormalizedUsername);
        Assert.Equal(EmployeeRole.ManagerAdmin, employee.Role);
        Assert.True(new Pbkdf2PinHasher().Verify(employee.PinHash, pin));
        Assert.DoesNotContain(pin, output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(employee.PinHash, output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Server=", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnyEmployeeExists_RefusesWithoutModification()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var context = lease.CreateDbContext();
        context.Employees.Add(NewEmployee("existing"));
        await context.SaveChangesAsync();
        var command = CreateCommand(context, "7391");

        var exitCode = await command.ExecuteAsync(new StringWriter(), default);

        Assert.Equal(BootstrapAdminCommand.RefusedExitCode, exitCode);
        var employee = Assert.Single(await context.Employees.AsNoTracking().ToListAsync());
        Assert.Equal("EXISTING", employee.NormalizedUsername);
    }

    [Fact]
    public async Task ExecuteAsync_SecondInvocationIsRefusedAndLeavesFirstManagerUnchanged()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var firstContext = lease.CreateDbContext();
        var firstCommand = CreateCommand(firstContext, "7391");
        Assert.Equal(
            BootstrapAdminCommand.SuccessExitCode,
            await firstCommand.ExecuteAsync(new StringWriter(), default)
        );
        var original = Assert.Single(await firstContext.Employees.AsNoTracking().ToListAsync());

        await using var secondContext = lease.CreateDbContext();
        var secondCommand = CreateCommand(secondContext, "1111");
        var exitCode = await secondCommand.ExecuteAsync(new StringWriter(), default);

        Assert.Equal(BootstrapAdminCommand.RefusedExitCode, exitCode);
        var persisted = Assert.Single(await secondContext.Employees.AsNoTracking().ToListAsync());
        Assert.Equal(original.Id, persisted.Id);
        Assert.Equal(original.PinHash, persisted.PinHash);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentBootstrapAttemptsOnSameEmptyDatabase_ExactlyOneSucceeds()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var firstContext = lease.CreateDbContext();
        await using var secondContext = lease.CreateDbContext();
        var firstCommand = CreateCommand(firstContext, "1111", "firstmanager");
        var secondCommand = CreateCommand(secondContext, "2222", "secondmanager");

        var exitCodes = await Task.WhenAll(
            firstCommand.ExecuteAsync(new StringWriter(), default),
            secondCommand.ExecuteAsync(new StringWriter(), default)
        );

        Assert.Equal(1, exitCodes.Count(code => code == BootstrapAdminCommand.SuccessExitCode));
        Assert.Equal(1, exitCodes.Count(code => code == BootstrapAdminCommand.RefusedExitCode));
        await using var verification = lease.CreateDbContext();
        Assert.Single(await verification.Employees.AsNoTracking().ToListAsync());
    }

    private static BootstrapAdminCommand CreateCommand(
        LootSinglesDbContext context,
        string pin,
        string username = "bootstrapmanager"
    )
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["BootstrapAdmin:Username"] = username,
                    ["BootstrapAdmin:DisplayName"] = "Bootstrap Manager",
                    ["BootstrapAdmin:Pin"] = pin,
                }
            )
            .Build();
        var repository = new EmployeeRepository(context);
        var service = new BootstrapAdminService(repository, new Pbkdf2PinHasher());
        return new BootstrapAdminCommand(configuration, context, service);
    }

    private static Employee NewEmployee(string username) =>
        new()
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = username,
            PinHash = new Pbkdf2PinHasher().Hash("1234"),
            Role = EmployeeRole.Picker,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
