using LootSingles.Domain.Orders;

namespace LootSingles.Application.Orders;

public sealed record OrderDetail(
    int OrderId,
    string TcgplayerOrderId,
    OrderStatus Status,
    IReadOnlyList<OrderLineDetail> Lines,
    // Required, with no default: a construction that forgot it would otherwise fail open into
    // third-party catalog enrichment for an API-imported order (TCGplayer API agreement).
    OrderImportSource ImportSource,
    int? ClaimedByEmployeeId = null,
    string? ClaimedByEmployeeName = null
);

public sealed record OrderLineDetail(
    int Id,
    PickOutcome? PickOutcome,
    string ProductName,
    string ProductLine,
    string Set,
    string? CollectorNumber,
    string? Rarity,
    string? Variant,
    string Condition,
    int Quantity,
    string? ImageUrl = null,
    PickingIssueDetail? CurrentIssue = null,
    string? Language = null
);

public sealed record PickingIssueDetail(
    PickingIssueType IssueType,
    int? RequiredQuantity,
    int? FoundQuantity,
    string? Note,
    string? ReportedByEmployeeName,
    DateTimeOffset ReportedAt
);
