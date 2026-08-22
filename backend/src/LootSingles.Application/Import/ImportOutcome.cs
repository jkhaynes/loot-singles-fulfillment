namespace LootSingles.Application.Import;

/// <summary>
/// Represents the outcome of importing a single order from a TCGplayer packing slip.
/// </summary>
public enum ImportOutcome
{
    /// <summary>The order was successfully imported and a corresponding Order entity was created.</summary>
    Succeeded,

    /// <summary>The order could not be imported due to validation failures or missing required data.</summary>
    Rejected,
}
