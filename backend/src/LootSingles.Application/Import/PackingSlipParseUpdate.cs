namespace LootSingles.Application.Import;

public sealed record PackingSlipParseUpdate(
    int OrdersDetected,
    bool IsComplete,
    ParsedPackingSlip? PackingSlip);
