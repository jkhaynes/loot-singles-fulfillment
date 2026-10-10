using System.Text.RegularExpressions;

namespace LootSingles.Application.Import;

public static partial class OrderLineExtractor
{
    /// <summary>
    /// Turns a parsed packing-slip block into an <see cref="OrderCandidate"/>. Source-specific
    /// rejections (unreadable quantity, an unparseable description, no collector number) are
    /// reported through <see cref="OrderCandidate.RejectedBySource"/> with the importer's original
    /// messages; everything else is left to <see cref="OrderCandidateValidator"/>.
    /// </summary>
    public static OrderCandidate Extract(RawOrderBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        // A missing identifier or an empty block is the validator's call; it checks both before
        // looking at any line.
        if (string.IsNullOrWhiteSpace(block.OrderIdentifier) || block.ProductLines.Count == 0)
            return new OrderCandidate(block.OrderIdentifier, []);

        var lines = new List<OrderLineCandidate>();
        foreach (var source in block.ProductLines)
        {
            if (!int.TryParse(source.QuantityText, out var quantity))
                return Rejected(
                    block,
                    lines,
                    (
                        FailureType.InvalidQuantity,
                        $"Quantity '{source.QuantityText}' is not a positive whole number for product line '{source.RawDescription}'."
                    )
                );

            var (line, failure) = ExtractLine(source, quantity);
            if (failure is { } extractionFailure)
            {
                // A parsed quantity of zero or less outranks the description problem, as it
                // always has: hand the validator a line that carries only the quantity.
                if (quantity <= 0)
                {
                    lines.Add(QuantityOnlyLine(source, quantity));
                    return new OrderCandidate(block.OrderIdentifier, lines);
                }

                return Rejected(
                    block,
                    lines,
                    (
                        extractionFailure.Type,
                        $"Order '{block.OrderIdentifier}': {extractionFailure.Message}"
                    )
                );
            }

            lines.Add(line!);
        }

        return new OrderCandidate(block.OrderIdentifier, lines);
    }

    // The failure of an earlier line must still win over this one, so when the lines read so far
    // already fail validation, return them without the rejection and let the validator report it.
    private static OrderCandidate Rejected(
        RawOrderBlock block,
        List<OrderLineCandidate> linesSoFar,
        (FailureType Type, string Message) rejection
    )
    {
        var earlier = new OrderCandidate(block.OrderIdentifier, linesSoFar);
        return linesSoFar.Count > 0 && OrderCandidateValidator.Validate(earlier) is not null
            ? earlier
            : earlier with
            {
                RejectedBySource = rejection,
            };
    }

    private static OrderLineCandidate QuantityOnlyLine(RawProductLine source, int quantity) =>
        new(source.RawDescription, null, null, null, null, null, null, null, null, null, quantity);

    private static (
        OrderLineCandidate? Line,
        (FailureType Type, string Message)? Failure
    ) ExtractLine(RawProductLine source, int quantity)
    {
        var segments = source.RawDescription.Split(" - ", StringSplitOptions.TrimEntries);
        if (segments.Length < 4)
        {
            return (
                null,
                (
                    FailureType.MissingProductName,
                    $"Product description has an unexpected format: '{source.RawDescription}'."
                )
            );
        }

        var collectorIndex = Array.FindIndex(
            segments,
            2,
            segment => CollectorNumberPattern().IsMatch(segment)
        );
        if (collectorIndex < 0)
        {
            return (
                null,
                (
                    FailureType.MissingCollectorNumber,
                    $"Product description has no collector number: '{source.RawDescription}'."
                )
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
            return (
                null,
                (
                    FailureType.MissingProductName,
                    $"Product description has no set/product separator: '{source.RawDescription}'."
                )
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

        var candidate = new OrderLineCandidate(
            RawDescription: source.RawDescription,
            ProductLine: segments[0],
            ProductName: productName,
            Set: set,
            CollectorNumber: segments[collectorIndex],
            Rarity: collectorIndex < segments.Length - 2
                ? string.Join(
                    " - ",
                    segments.Skip(collectorIndex + 1).Take(segments.Length - collectorIndex - 2)
                )
                : null,
            Condition: conditionAndVariant.Condition,
            Variant: conditionAndVariant.Variant,
            Language: null,
            ImageUrl: null,
            Quantity: quantity
        );
        return (candidate, null);
    }

    [GeneratedRegex(@"\([^()]+\)", RegexOptions.CultureInvariant)]
    private static partial Regex ParentheticalMarkerPattern();

    [GeneratedRegex(@"^#[A-Za-z0-9/.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex CollectorNumberPattern();
}
