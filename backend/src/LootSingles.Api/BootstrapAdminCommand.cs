using LootSingles.Application.Auth;
using LootSingles.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.Api;

public sealed class BootstrapAdminCommand(
    IConfiguration configuration,
    LootSinglesDbContext context,
    BootstrapAdminService service
)
{
    public const int SuccessExitCode = 0;
    public const int FailureExitCode = 1;
    public const int RefusedExitCode = 2;
    public const int InvalidConfigurationExitCode = 3;

    public async Task<int> ExecuteAsync(TextWriter output, CancellationToken cancellationToken)
    {
        var username = configuration["BootstrapAdmin:Username"];
        var displayName = configuration["BootstrapAdmin:DisplayName"];
        var pin = configuration["BootstrapAdmin:Pin"];

        if (
            string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(displayName)
            || !PinFormat.IsValid(pin)
        )
        {
            await output.WriteLineAsync(
                "Bootstrap configuration is missing or invalid; no employee was created."
            );
            return InvalidConfigurationExitCode;
        }

        try
        {
            await context.Database.MigrateAsync(cancellationToken);
            var result = await service.BootstrapAsync(
                username,
                displayName,
                pin,
                cancellationToken
            );
            switch (result)
            {
                case BootstrapAdminOutcome.Success:
                    await output.WriteLineAsync("Initial Manager/Admin employee created.");
                    return SuccessExitCode;
                case BootstrapAdminOutcome.EmployeesAlreadyExist:
                    await output.WriteLineAsync(
                        "Bootstrap refused because the employee store is not empty."
                    );
                    return RefusedExitCode;
                default:
                    await output.WriteLineAsync(
                        "Bootstrap configuration is missing or invalid; no employee was created."
                    );
                    return InvalidConfigurationExitCode;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await output.WriteLineAsync(
                "Bootstrap failed while preparing or updating the database; sensitive details were omitted."
            );
            return FailureExitCode;
        }
    }
}
