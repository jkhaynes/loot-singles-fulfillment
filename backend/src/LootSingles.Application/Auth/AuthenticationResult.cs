using LootSingles.Domain.Employees;

namespace LootSingles.Application.Auth;

/// <summary>
/// The outcome of an <see cref="AuthenticationService.LoginAsync"/> attempt.
/// </summary>
public enum AuthenticationOutcome
{
    Success,
    InvalidCredentials,
    AccountLocked,
}

/// <summary>
/// The typed result of a login attempt (Constitution XII). <see cref="Employee"/> is set only
/// when <see cref="Outcome"/> is <see cref="AuthenticationOutcome.Success"/>.
/// </summary>
public sealed record AuthenticationResult(AuthenticationOutcome Outcome, Employee? Employee = null)
{
    public static AuthenticationResult Success(Employee employee) =>
        new(AuthenticationOutcome.Success, employee);

    public static readonly AuthenticationResult InvalidCredentials =
        new(AuthenticationOutcome.InvalidCredentials);

    public static readonly AuthenticationResult AccountLocked =
        new(AuthenticationOutcome.AccountLocked);
}
