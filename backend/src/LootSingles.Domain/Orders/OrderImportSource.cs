namespace LootSingles.Domain.Orders;

/// <summary>
/// Where an order, or an import attempt, came from. Stored as an integer; existing rows are
/// <see cref="PackingSlipPdf"/>.
/// </summary>
public enum OrderImportSource
{
    /// <summary>Imported from a TCGplayer packing slip PDF.</summary>
    PackingSlipPdf = 0,

    /// <summary>Imported from the TCGplayer API.</summary>
    TcgplayerApi = 1,
}
