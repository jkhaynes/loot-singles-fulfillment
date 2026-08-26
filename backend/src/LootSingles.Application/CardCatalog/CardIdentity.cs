namespace LootSingles.Application.CardCatalog;

public sealed record CardIdentity(
    string ProductName,
    string Set,
    string CollectorNumber,
    string? Variant
);
