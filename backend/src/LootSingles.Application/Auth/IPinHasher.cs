namespace LootSingles.Application.Auth;

/// <summary>
/// Hashes and verifies employee PINs (FR-003). The Application/Domain layers depend on this
/// abstraction rather than directly on the underlying hashing primitive (Constitution IX/XII).
/// </summary>
public interface IPinHasher
{
    /// <summary>
    /// Produces a secure hash of the given PIN. The result never contains the raw PIN.
    /// </summary>
    string Hash(string pin);

    /// <summary>
    /// Returns true when <paramref name="suppliedPin"/> matches the PIN that produced <paramref name="pinHash"/>.
    /// </summary>
    bool Verify(string pinHash, string suppliedPin);
}
