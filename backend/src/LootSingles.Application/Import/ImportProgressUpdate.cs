namespace LootSingles.Application.Import;

public sealed record ImportProgressUpdate(
    int OrdersDetected,
    int OrdersProcessed,
    int SucceededCount,
    int FailedCount,
    bool IsComplete,
    ImportAttempt ImportAttempt
);
