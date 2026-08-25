using System.Text.RegularExpressions;
using LootSingles.Domain.Orders;

namespace LootSingles.Application.Import;

public static partial class OrderLineExtractor
{
    public static OrderLineValidationResult Extract(RawProductLine source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var segments = source.RawDescription.Split(" - ", StringSplitOptions.TrimEntries);
        if (segments.Length < 4)
        {
            return OrderLineValidationResult.Invalid(
                FailureType.MissingProductName,
                $"Product description has an unexpected format: '{source.RawDescription}'."
            );
        }

        var collectorIndex = Array.FindIndex(
            segments,
            2,
            segment => CollectorNumberPattern().IsMatch(segment)
        );
        if (collectorIndex < 0)
        {
            return OrderLineValidationResult.Invalid(
                FailureType.MissingCollectorNumber,
                $"Product description has no collector number: '{source.RawDescription}'."
            );
        }

        var setAndProductSegments = segments.Skip(1).Take(collectorIndex - 1).ToArray();

        // TCGplayer sometimes prints the collector number twice for certain rarities - once as a
        // bare, unprefixed echo immediately before the "#"-prefixed collector number segment
        // itself (e.g. "Fomantis - 085/084 - #085/084 - ..."). When the segment right before the
        // true collector number is an exact match for it (minus the "#"), it's a redundant echo,
        // not part of the set or product name - drop it.
        if (
            setAndProductSegments.Length > 0
            && string.Equals(
                setAndProductSegments[^1],
                segments[collectorIndex].TrimStart('#'),
                StringComparison.Ordinal
            )
        )
        {
            setAndProductSegments = setAndProductSegments[..^1];
        }

        var setAndProduct = string.Join(" - ", setAndProductSegments);
        var separator = setAndProduct.LastIndexOf(':');

        string set;
        string productName;
        if (separator >= 0)
        {
            // Colon-delimited set/product, e.g. Pokémon's "SV: Black Bolt: Genesect ex".
            set = setAndProduct[..separator].Trim();
            productName = setAndProduct[(separator + 1)..].Trim();
        }
        else if (setAndProductSegments.Length >= 2)
        {
            // No colon: the first segment is the set, and the card name — which may itself
            // legitimately contain " - " (e.g. Disney Lorcana's "Scrooge McDuck - S.H.U.S.H.
            // Agent") — is everything remaining before the collector number.
            set = setAndProductSegments[0].Trim();
            productName = string.Join(" - ", setAndProductSegments.Skip(1)).Trim();
        }
        else
        {
            return OrderLineValidationResult.Invalid(
                FailureType.MissingProductName,
                $"Product description has no set/product separator: '{source.RawDescription}'."
            );
        }

        var markers = ParentheticalMarkerPattern()
            .Matches(productName)
            .Select(match => match.Value)
            .ToArray();
        var conditionAndVariant = ConditionVariantParser.Parse(
            segments[^1],
            markers.Length == 0 ? null : string.Join(", ", markers)
        );

        var orderLine = new OrderLine
        {
            RawDescription = source.RawDescription,
            ProductLine = segments[0],
            Set = set,
            ProductName = productName,
            CollectorNumber = segments[collectorIndex],
            Rarity =
                collectorIndex < segments.Length - 2
                    ? string.Join(
                        " - ",
                        segments.Skip(collectorIndex + 1).Take(segments.Length - collectorIndex - 2)
                    )
                    : null,
            Condition = conditionAndVariant.Condition ?? string.Empty,
            Variant = conditionAndVariant.Variant,
            Quantity = int.Parse(source.QuantityText),
        };
        return new OrderLineValidationResult(orderLine, null, null);
    }

    [GeneratedRegex(@"\([^()]+\)", RegexOptions.CultureInvariant)]
    private static partial Regex ParentheticalMarkerPattern();

    [GeneratedRegex(@"^#[A-Za-z0-9/.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex CollectorNumberPattern();
}
