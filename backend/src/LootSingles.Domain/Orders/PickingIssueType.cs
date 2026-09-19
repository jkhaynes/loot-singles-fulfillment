namespace LootSingles.Domain.Orders;

/// <summary>
/// The kind of problem a picker reported on an order line (PRD §19.3, 015-pick-completion FR-002).
/// Stored as a string for readability in the database.
/// </summary>
public enum PickingIssueType
{
    CardNotFound,
    InsufficientQuantity,
    WrongCardInLocation,
    WrongVariant,
    WrongCondition,
    Damaged,
    InventoryDiscrepancy,
    InformationIncorrect,
    Other,
}
