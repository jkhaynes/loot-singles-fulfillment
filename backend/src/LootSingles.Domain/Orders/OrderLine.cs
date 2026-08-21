namespace LootSingles.Domain.Orders;

/// <summary>
/// Represents a single product line (card item) within a TCGplayer order.
/// An OrderLine is always part of exactly one Order and captures all product information
/// extracted from the packing slip, including both structured fields and the raw description.
/// </summary>
public class OrderLine
{
    /// <summary>
    /// Primary key, auto-incremented identity.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the parent Order. Each OrderLine belongs to exactly one Order.
    /// </summary>
    public int OrderId { get; set; }

    /// <summary>
    /// The complete raw product description text exactly as it appeared in the packing slip, unmodified.
    /// This is retained alongside the structured fields for reference and audit purposes.
    /// </summary>
    public required string RawDescription { get; set; }

    /// <summary>
    /// The game or product line (e.g., "Pokemon", "Magic", "Lorcana TCG").
    /// Captured as printed in the source material, not normalized.
    /// </summary>
    public required string ProductLine { get; set; }

    /// <summary>
    /// The product/card name (e.g., "Pikachu", "Black Lotus").
    /// Required for a successfully-created OrderLine.
    /// </summary>
    public required string ProductName { get; set; }

    /// <summary>
    /// The set identifier (e.g., "Base Set", "Unlimited Edition").
    /// Required for a successfully-created OrderLine.
    /// </summary>
    public required string Set { get; set; }

    /// <summary>
    /// The collector number as printed in the source, including the '#' prefix and any '/xxx' suffix
    /// (e.g., "#067/086", "#16").
    /// Not stripped or reformatted.
    /// </summary>
    public required string CollectorNumber { get; set; }

    /// <summary>
    /// The card's rarity tier (e.g., "Common", "Rare Holo", "Mythic Rare", "Double Rare").
    /// Captured as printed in the source material, not normalized.
    /// Optional — absence does not prevent an OrderLine from existing.
    /// </summary>
    public string? Rarity { get; set; }

    /// <summary>
    /// The card condition/grade (e.g., "Near Mint", "Lightly Played").
    /// Represents the condition alone, not combined with variant information.
    /// Required for a successfully-created OrderLine.
    /// </summary>
    public required string Condition { get; set; }

    /// <summary>
    /// The variant/edition qualifier (e.g., "Holofoil", "Foil", "Holofoil, (Showcase)").
    /// Preserves everything present in the source when it exists.
    /// Optional — absence does not prevent an OrderLine from existing.
    /// </summary>
    public string? Variant { get; set; }

    /// <summary>
    /// The quantity of this item ordered. A positive whole number.
    /// Preserved exactly as imported, no rounding or capping.
    /// </summary>
    public required int Quantity { get; set; }
}
