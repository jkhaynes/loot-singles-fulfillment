using LootSingles.Domain.Orders;

namespace LootSingles.Application.Packing;

/// <summary>
/// One employee who recorded a pick outcome on an order.
/// </summary>
public sealed record LabelContributor(int EmployeeId, string DisplayName);

/// <summary>
/// Everything printed on an order's label, derived from the order itself
/// (017-pick-completion-handoff FR-017).
/// <para>
/// Nothing here is stored. A snapshot could drift from the order it identifies; a derivation
/// cannot, which is also what makes a reprint match the original without a second copy existing
/// (FR-016).
/// </para>
/// </summary>
public sealed record LabelContent(
    int OrderId,
    string TcgplayerOrderId,
    /// <summary>Physical cards pulled — the only count on the label (FR-010, FR-004).</summary>
    int CardCount,
    /// <summary>
    /// Every employee who recorded an outcome, ordered by when they first contributed (FR-041).
    /// A list rather than one name: an order released and re-claimed genuinely has several, and
    /// choosing between them would mean inventing a tiebreak nobody decided.
    /// </summary>
    IReadOnlyList<LabelContributor> PickedBy,
    /// <summary>When the most recent outcome was recorded — when picking finished (FR-042).</summary>
    DateTimeOffset? PickedAt,
    /// <summary>Whether this is a hold label (FR-013).</summary>
    bool IsHeld,
    /// <summary>The products blocking the order, for the needs-a-manager ending (FR-003).</summary>
    IReadOnlyList<string> UnresolvedProducts,
    /// <summary>
    /// Cards set aside with the order. Always null in this feature: nothing records one yet, and
    /// printing a zero would tell a manager "no cards set aside" on every hold label (FR-046).
    /// </summary>
    int? SetAsideCount,
    /// <summary>
    /// Whether the order ships short. Always false here — write-offs arrive with the
    /// issue-resolution feature (FR-014).
    /// </summary>
    bool ShipsShort,
    /// <summary>
    /// Whether any outcome has been recorded at all. This, not the order's status, is what decides
    /// whether there is a label to print: a held order is never <c>Picked</c> and must still print
    /// one (FR-009).
    /// </summary>
    bool HasStarted
)
{
    /// <param name="employeeDisplayNames">
    /// Display names for the employees who recorded outcomes. Passed in rather than read through a
    /// navigation so this stays a pure derivation, testable without a database.
    /// </param>
    public static LabelContent From(
        Order order,
        IReadOnlyDictionary<int, string> employeeDisplayNames
    )
    {
        var lines = order.OrderLines;

        var contributors = lines
            .Where(line => line.PickOutcomeRecordedByEmployeeId is not null)
            .GroupBy(line => line.PickOutcomeRecordedByEmployeeId!.Value)
            .OrderBy(group => group.Min(line => line.PickOutcomeRecordedAt))
            .Select(group => new LabelContributor(
                group.Key,
                employeeDisplayNames.TryGetValue(group.Key, out var name) ? name : "Unknown"
            ))
            .ToList();

        var recordedAt = lines
            .Where(line => line.PickOutcomeRecordedAt is not null)
            .Select(line => line.PickOutcomeRecordedAt!.Value)
            .ToList();

        return new LabelContent(
            OrderId: order.Id,
            TcgplayerOrderId: order.TcgplayerOrderId,
            CardCount: lines
                .Where(line => line.PickOutcome == PickOutcome.Picked)
                .Sum(line => line.Quantity),
            PickedBy: contributors,
            PickedAt: recordedAt.Count == 0 ? null : recordedAt.Max(),
            // Anything not picked is unresolved: a reported issue, and equally a product nobody
            // looked at. The review lets a picker finish with those (016 FR-019a), and counting
            // only reported issues ended such a pick "complete" with a ready-to-pack label for a
            // short sleeve (016 FR-019; branch review round 3, BR-001).
            IsHeld: lines.Any(line => line.PickOutcome != PickOutcome.Picked),
            UnresolvedProducts: lines
                .Where(line => line.PickOutcome != PickOutcome.Picked)
                .Select(line => line.ProductName)
                .ToList(),
            SetAsideCount: null,
            ShipsShort: false,
            HasStarted: lines.Any(line => line.PickOutcome is not null)
        );
    }
}
