using LootSingles.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.Api;

/// <summary>
/// Applies pending EF Core migrations and exits. Run as its own one-shot job before a new version
/// begins serving requests (019 FR-015), never by the application at startup.
///
/// Running migrations here rather than in <c>Program.cs</c>'s startup path is what lets the running
/// application hold no schema permission at all (FR-021): only the identity this job runs under has
/// <c>db_ddladmin</c>. It also means a failed migration fails a visible job instead of crash-looping
/// a container.
///
/// Mirrors <see cref="BootstrapAdminCommand"/>, which established this shape.
/// </summary>
public sealed class MigrateCommand(LootSinglesDbContext context, ILogger<MigrateCommand> logger)
{
    public const int SuccessExitCode = 0;
    public const int FailureExitCode = 1;

    public async Task<int> ExecuteAsync(TextWriter output, CancellationToken cancellationToken)
    {
        try
        {
            var pending = (
                await context.Database.GetPendingMigrationsAsync(cancellationToken)
            ).ToArray();

            if (pending.Length == 0)
            {
                // The common case: most releases carry no schema change. Saying so explicitly beats
                // silence, because an operator reading a deploy log needs to tell "nothing to do"
                // apart from "did not run".
                logger.LogInformation("No pending migrations; the database is already current.");
                await output.WriteLineAsync("No pending migrations.");
                return SuccessExitCode;
            }

            logger.LogInformation(
                "Applying {MigrationCount} pending migration(s): {Migrations}.",
                pending.Length,
                string.Join(", ", pending)
            );

            await context.Database.MigrateAsync(cancellationToken);

            logger.LogInformation(
                "Applied {MigrationCount} migration(s): {Migrations}.",
                pending.Length,
                string.Join(", ", pending)
            );
            await output.WriteLineAsync(
                $"Applied {pending.Length} migration(s): {string.Join(", ", pending)}."
            );
            return SuccessExitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The exception goes to the log, where an operator can reach it. It does NOT go to
            // stdout: this command runs inside a deployment workflow whose output is retained, and a
            // connection string or server name echoed there is a durable leak (FR-019, FR-022).
            logger.LogError(exception, "Migration failed.");
            await output.WriteLineAsync(
                "Migration failed; see the application log for details. Sensitive details were omitted."
            );
            return FailureExitCode;
        }
    }
}
