using LootSingles.Application.Import;

namespace LootSingles.Infrastructure.Tcgplayer;

/// <summary>
/// Turns one TCGplayer order (its details row, all of its de-paged items and the catalog lookups)
/// into the source-neutral <see cref="OrderCandidate"/> (research.md section 7). Pure: no I/O.
/// TCGplayer's own values are authoritative, so names pass through verbatim and anything missing
/// stays null for the shared <see cref="OrderCandidateValidator"/> to judge; nothing is guessed.
/// </summary>
public static class TcgplayerOrderTranslator
{
    private const string NormalPrinting = "Normal";
    private const string Foil = "Foil";
    private const string VariantSeparator = ", ";

    /// <param name="order">The order's details row; its <c>productCount</c> is total units.</param>
    /// <param name="items">Every item of the order, already gathered from all pages.</param>
    /// <param name="reportedLineCount">The items endpoint's <c>totalItems</c>, which counts lines.</param>
    /// <param name="skusById">Catalog SKUs found; a missing entry means "not found", not an error.</param>
    /// <param name="productsById">Catalog products found; a missing entry means "not found".</param>
    /// <param name="options">Supplies the <c>extendedData</c> names for the number and rarity.</param>
    public static OrderCandidate Translate(
        TcgplayerOrderDetails order,
        IReadOnlyList<TcgplayerOrderItem> items,
        int reportedLineCount,
        IReadOnlyDictionary<int, TcgplayerSku> skusById,
        IReadOnlyDictionary<int, TcgplayerProduct> productsById,
        TcgplayerOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(skusById);
        ArgumentNullException.ThrowIfNull(productsById);
        ArgumentNullException.ThrowIfNull(options);

        var lines = items
            .Select(item => TranslateLine(item, FindProduct(item, skusById, productsById), options))
            .ToList();
        var candidate = new OrderCandidate(order.OrderNumber, lines);

        // Without an order number there is nothing to name in a message; the validator reports it.
        if (string.IsNullOrWhiteSpace(order.OrderNumber))
            return candidate;

        return CompletenessFailure(order, items, reportedLineCount) is { } message
            ? candidate with
            {
                RejectedBySource = (
                    FailureType.IncompleteOrder,
                    $"Order '{order.OrderNumber}': {message}"
                ),
            }
            : candidate;
    }

    // research.md sections 4 and 5: every line retrieved, and their units add up to productCount.
    private static string? CompletenessFailure(
        TcgplayerOrderDetails order,
        IReadOnlyList<TcgplayerOrderItem> items,
        int reportedLineCount
    )
    {
        if (items.Count != reportedLineCount)
            return $"TCGplayer reported {reportedLineCount} lines but {items.Count} were retrieved.";

        if (order.ProductCount is not { } productCount)
            return "TCGplayer reported no product count to check the lines against.";

        // A missing or non-positive quantity is rejected by the validator as InvalidQuantity,
        // which names the line; a sum over it would only blur that into a count mismatch.
        if (items.Any(item => item.Quantity is not > 0))
            return null;

        var units = items.Sum(item => item.Quantity!.Value);
        return units == productCount
            ? null
            : $"line quantities add up to {units}, but TCGplayer reports a product count of {productCount}.";
    }

    private static TcgplayerProduct? FindProduct(
        TcgplayerOrderItem item,
        IReadOnlyDictionary<int, TcgplayerSku> skusById,
        IReadOnlyDictionary<int, TcgplayerProduct> productsById
    ) =>
        item.SkuId is { } skuId
        && skusById.TryGetValue(skuId, out var sku)
        && sku.ProductId is { } productId
        && productsById.TryGetValue(productId, out var product)
            ? product
            : null;

    private static OrderLineCandidate TranslateLine(
        TcgplayerOrderItem item,
        TcgplayerProduct? product,
        TcgplayerOptions options
    )
    {
        var conditionText = NullIfBlank(item.Condition);
        var parsed = conditionText is null
            ? new ConditionVariant(null, null)
            : ConditionVariantParser.Parse(conditionText);

        var number = ExtendedValue(product, options.CollectorNumberField);
        var collectorNumber =
            number is null ? null
            : number.StartsWith('#') ? number
            : $"#{number}";
        var rarity = NullIfBlank(item.Rarity) ?? ExtendedValue(product, options.RarityField);
        var variant = ComposeVariant(parsed.Variant, item.Printing, item.IsFoil == true);
        var language = NullIfBlank(item.Language);

        var rawDescription = string.Join(
            " - ",
            new[]
            {
                item.CategoryName,
                item.GroupName,
                item.ProductName,
                collectorNumber,
                rarity,
                parsed.Condition,
                variant,
                language,
            }.Where(value => !string.IsNullOrWhiteSpace(value))
        );

        return new OrderLineCandidate(
            RawDescription: rawDescription,
            ProductLine: item.CategoryName,
            ProductName: item.ProductName,
            Set: item.GroupName,
            CollectorNumber: collectorNumber,
            Rarity: rarity,
            Condition: parsed.Condition,
            Variant: variant,
            Language: language,
            ImageUrl: NullIfBlank(item.ProductImageUrl) ?? NullIfBlank(product?.ImageUrl),
            Quantity: item.Quantity
        );
    }

    // The condition's suffix first, then the printing unless it is "Normal" or already there, then
    // "Foil" when isFoil is set and nothing so far names a foil. Live foil lines report the foil
    // all three ways at once, so each source only adds what the earlier ones did not.
    private static string? ComposeVariant(string? conditionSuffix, string? printing, bool isFoil)
    {
        var parts = new List<string>();
        if (conditionSuffix is not null)
            parts.Add(conditionSuffix);

        var printingText = NullIfBlank(printing);
        if (
            printingText is not null
            && !printingText.Equals(NormalPrinting, StringComparison.OrdinalIgnoreCase)
            && !parts.Any(part => part.Equals(printingText, StringComparison.OrdinalIgnoreCase))
        )
        {
            parts.Add(printingText);
        }

        if (isFoil && !parts.Any(part => part.Contains(Foil, StringComparison.OrdinalIgnoreCase)))
            parts.Add(Foil);

        return parts.Count == 0 ? null : string.Join(VariantSeparator, parts);
    }

    private static string? ExtendedValue(TcgplayerProduct? product, string name) =>
        NullIfBlank(
            product
                ?.ExtendedData?.FirstOrDefault(entry =>
                    string.Equals(entry.Name, name, StringComparison.Ordinal)
                )
                ?.Value
        );

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
