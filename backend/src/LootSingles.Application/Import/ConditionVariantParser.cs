namespace LootSingles.Application.Import;

public static class ConditionVariantParser
{
    private static readonly string[] KnownConditions =
    [
        "Moderately Played",
        "Lightly Played",
        "Heavily Played",
        "Near Mint",
        "Damaged",
    ];

    public static ConditionVariant Parse(string source, string? parentheticalMarker = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var condition = KnownConditions.FirstOrDefault(value =>
            source.Equals(value, StringComparison.OrdinalIgnoreCase)
            || source.StartsWith($"{value} ", StringComparison.OrdinalIgnoreCase));

        if (condition is null)
        {
            return new ConditionVariant(null, CombineVariants(source, parentheticalMarker));
        }

        var suffix = source[condition.Length..].Trim();
        return new ConditionVariant(
            condition,
            CombineVariants(string.IsNullOrEmpty(suffix) ? null : suffix, parentheticalMarker));
    }

    private static string? CombineVariants(params string?[] values)
    {
        var variants = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return variants.Length == 0 ? null : string.Join(", ", variants);
    }
}

public sealed record ConditionVariant(string? Condition, string? Variant);
