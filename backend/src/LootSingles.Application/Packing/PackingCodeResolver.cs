using System.Globalization;

namespace LootSingles.Application.Packing;

/// <summary>
/// What the packing desk's input resolved to: at most one of an order number or a TCGplayer
/// identifier, and possibly neither.
/// </summary>
public sealed record PackingCode(int? OrderId, string? TcgplayerOrderId)
{
    public bool IsResolvable => OrderId is not null || TcgplayerOrderId is not null;
}

/// <summary>
/// Turns whatever lands in the packing desk's box into a lookup key
/// (017-pick-completion-handoff FR-024, research.md §10).
/// <para>
/// Three shapes arrive, because a scanner is a keyboard and types exactly what is encoded: a link
/// from the label's QR, a bare order number typed by a person, and the full TCGplayer identifier
/// from the Code 128. Normalising once at this boundary keeps the rule in one place.
/// </para>
/// </summary>
public static class PackingCodeResolver
{
    public static PackingCode Resolve(string? input)
    {
        var trimmed = input?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return new PackingCode(null, null);
        }

        // A scanned QR arrives as the whole link, so reduce it to the order it points at. A link
        // whose last segment is not an order number points at no order — it is not silently
        // reinterpreted as a TCGplayer identifier, because "packing" is not an order id.
        if (trimmed.Contains('/'))
        {
            var segment = LastPathSegment(trimmed);

            return TryOrderId(segment, out var linkedId)
                ? new PackingCode(linkedId, null)
                : new PackingCode(null, null);
        }

        if (TryOrderId(trimmed, out var typedId))
        {
            return new PackingCode(typedId, null);
        }

        // Not a number and not a link: a TCGplayer identifier from the Code 128, or something
        // the lookup will honestly fail to find. Either way it is never coerced into an order.
        return new PackingCode(null, trimmed.ToUpperInvariant());
    }

    private static bool TryOrderId(string value, out int orderId) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out orderId);

    private static string LastPathSegment(string value)
    {
        var withoutQuery = value.Split('?')[0].Split('#')[0];

        return withoutQuery.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;
    }
}
