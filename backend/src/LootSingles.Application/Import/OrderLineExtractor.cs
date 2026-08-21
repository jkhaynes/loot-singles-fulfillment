using System.Text.RegularExpressions;
using LootSingles.Domain.Orders;

namespace LootSingles.Application.Import;

public static partial class OrderLineExtractor
{
    public static OrderLine Extract(RawProductLine source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var segments = source.RawDescription.Split(" - ", StringSplitOptions.TrimEntries);
        if (segments.Length < 4)
        {
            throw new OrderLineExtractionException(
                FailureType.MissingProductName,
                $"Product description has an unexpected format: '{source.RawDescription}'.");
        }

        var collectorIndex = Array.FindIndex(
            segments,
            2,
            segment => CollectorNumberPattern().IsMatch(segment));
        if (collectorIndex < 0)
        {
            throw new OrderLineExtractionException(
                FailureType.MissingCollectorNumber,
                $"Product description has no collector number: '{source.RawDescription}'.");
        }

        var setAndProduct = string.Join(" - ", segments.Skip(1).Take(collectorIndex - 1));
        var separator = setAndProduct.LastIndexOf(':');
        if (separator < 0)
        {
            throw new OrderLineExtractionException(
                FailureType.MissingProductName,
                $"Product description has no set/product separator: '{source.RawDescription}'.");
        }

        var productName = setAndProduct[(separator + 1)..].Trim();
        var markers = ParentheticalMarkerPattern().Matches(productName)
            .Select(match => match.Value)
            .ToArray();
        var conditionAndVariant = ConditionVariantParser.Parse(
            segments[^1],
            markers.Length == 0 ? null : string.Join(", ", markers));

        return new OrderLine
        {
            RawDescription = source.RawDescription,
            ProductLine = segments[0],
            Set = setAndProduct[..separator].Trim(),
            ProductName = productName,
            CollectorNumber = segments[collectorIndex],
            Rarity = collectorIndex < segments.Length - 2
                ? string.Join(" - ", segments.Skip(collectorIndex + 1).Take(segments.Length - collectorIndex - 2))
                : null,
            Condition = conditionAndVariant.Condition ?? string.Empty,
            Variant = conditionAndVariant.Variant,
            Quantity = int.Parse(source.QuantityText),
        };
    }

    [GeneratedRegex(@"\([^()]+\)", RegexOptions.CultureInvariant)]
    private static partial Regex ParentheticalMarkerPattern();

    [GeneratedRegex(@"^#[A-Za-z0-9/.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex CollectorNumberPattern();
}
