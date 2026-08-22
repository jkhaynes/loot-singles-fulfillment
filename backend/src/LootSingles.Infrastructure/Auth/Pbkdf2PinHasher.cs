using LootSingles.Application.Auth;
using LootSingles.Domain.Employees;
using Microsoft.AspNetCore.Identity;

namespace LootSingles.Infrastructure.Auth;

/// <summary>
/// <see cref="IPinHasher"/> via ASP.NET Core's <see cref="PasswordHasher{TUser}"/> (PBKDF2-HMACSHA256),
/// used standalone rather than adopting the full ASP.NET Core Identity framework (research.md §1, §9).
/// </summary>
public class Pbkdf2PinHasher : IPinHasher
{
    private readonly PasswordHasher<Employee> _hasher = new();

    public string Hash(string pin) => _hasher.HashPassword(null!, pin);

    public bool Verify(string pinHash, string suppliedPin) =>
        _hasher.VerifyHashedPassword(null!, pinHash, suppliedPin)
        != PasswordVerificationResult.Failed;
}
