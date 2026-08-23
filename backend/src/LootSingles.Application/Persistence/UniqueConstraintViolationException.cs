namespace LootSingles.Application.Persistence;

/// <summary>
/// Thrown by a repository or persistence adapter when saving new data violates a unique
/// constraint or index — the single Application-layer translation of a provider-specific
/// duplicate-key database exception, so Domain/Application code never
/// depends on EF Core or a specific database provider directly (Constitution XII). Shared across
/// features that need this same translation (e.g., a duplicate TCGplayer order id, a duplicate
/// employee username) instead of one exception type per feature (Constitution XIII).
/// </summary>
public sealed class UniqueConstraintViolationException(string message, Exception innerException)
    : Exception(message, innerException);
