using LootSingles.Domain.Employees;

namespace LootSingles.Application.Auth;

public enum BootstrapAdminOutcome
{
    Success,
    InvalidInput,
    EmployeesAlreadyExist,
}

/// <summary>
/// Creates the first Manager/Admin account for an empty employee store. Later employee creation
/// remains the responsibility of the authenticated employee-management workflow.
/// </summary>
public sealed class BootstrapAdminService(IEmployeeRepository repository, IPinHasher pinHasher)
{
    public async Task<BootstrapAdminOutcome> BootstrapAsync(
        string? username,
        string? displayName,
        string? pin,
        CancellationToken cancellationToken
    )
    {
        if (
            string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(displayName)
            || !PinFormat.IsValid(pin)
        )
        {
            return BootstrapAdminOutcome.InvalidInput;
        }

        var employee = new Employee
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = displayName,
            PinHash = pinHasher.Hash(pin!),
            Role = EmployeeRole.ManagerAdmin,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var added = await repository.TryAddFirstEmployeeAsync(employee, cancellationToken);
        return added ? BootstrapAdminOutcome.Success : BootstrapAdminOutcome.EmployeesAlreadyExist;
    }
}
