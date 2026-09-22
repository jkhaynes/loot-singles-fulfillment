using LootSingles.Application.Packing;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;

namespace LootSingles.UnitTests.Packing;

/// <summary>
/// 017-pick-completion-handoff T015/T080 — what a label says, derived from the order itself so it
/// can never disagree with what it identifies (FR-017) and a reprint always matches the original
/// (FR-016).
/// </summary>
public sealed class LabelContentTests
{
    [Fact]
    public void CardCount_CountsPhysicalCards_NotProductLines()
    {
        // Three product lines, eight physical cards. The distinction is the entire point of the
        // completion screen: a picker counts cards in a sleeve, never "lines".
        var order = OrderWith(
            Line(quantity: 3, PickOutcome.Picked, employeeId: 1, at: At("09:00")),
            Line(quantity: 4, PickOutcome.Picked, employeeId: 1, at: At("09:01")),
            Line(quantity: 1, PickOutcome.Picked, employeeId: 1, at: At("09:02"))
        );

        var label = LabelContent.From(order, Names((1, "Jordan")));

        Assert.Equal(8, label.CardCount);
    }

    [Fact]
    public void CardCount_CountsOnlyWhatWasPulled()
    {
        // A held line's cards are not in the picker's hand, so they are not in the count.
        var order = OrderWith(
            Line(quantity: 5, PickOutcome.Picked, employeeId: 1, at: At("09:00")),
            Line(quantity: 2, PickOutcome.HasIssue, employeeId: 1, at: At("09:01")),
            Line(quantity: 3, pickOutcome: null, employeeId: null, at: null)
        );

        var label = LabelContent.From(order, Names((1, "Jordan")));

        Assert.Equal(5, label.CardCount);
    }

    [Fact]
    public void ProducesNoProductLineCount()
    {
        // FR-004: the label states one count, physical cards, because that is the only number a
        // picker can check against a sleeve. Its absence is a Product Owner decision, so it is
        // asserted rather than left to be re-added by someone who assumes it was an oversight.
        var counts = typeof(LabelContent)
            .GetProperties()
            .Select(property => property.Name)
            .Where(name => name.EndsWith("Count", StringComparison.Ordinal))
            .ToList();

        Assert.Equal([nameof(LabelContent.CardCount), nameof(LabelContent.SetAsideCount)], counts);
    }

    [Fact]
    public void PickedBy_ListsEveryContributor_OrderedByFirstContribution()
    {
        // An order released and re-claimed is picked by more than one person. Naming only one of
        // them would be inaccurate, not merely incomplete (FR-041).
        var order = OrderWith(
            Line(quantity: 1, PickOutcome.Picked, employeeId: 2, at: At("09:05")),
            Line(quantity: 1, PickOutcome.Picked, employeeId: 1, at: At("09:00")),
            Line(quantity: 1, PickOutcome.Picked, employeeId: 2, at: At("09:10"))
        );

        var label = LabelContent.From(order, Names((1, "Jordan"), (2, "Sam")));

        Assert.Equal(["Jordan", "Sam"], label.PickedBy.Select(person => person.DisplayName));
    }

    [Fact]
    public void PickedBy_IncludesWhoeverReportedAnIssue()
    {
        // Reporting an issue is recording a pick outcome, and it is picker work. FR-044 derives
        // contributors from outcomes precisely so the list stays picking work and nothing else.
        var order = OrderWith(
            Line(quantity: 1, PickOutcome.Picked, employeeId: 1, at: At("09:00")),
            Line(quantity: 1, PickOutcome.HasIssue, employeeId: 2, at: At("09:01"))
        );

        var label = LabelContent.From(order, Names((1, "Jordan"), (2, "Sam")));

        Assert.Equal(["Jordan", "Sam"], label.PickedBy.Select(person => person.DisplayName));
    }

    [Fact]
    public void PickedAt_IsWhenPickingFinished_NotWhenItStarted()
    {
        var order = OrderWith(
            Line(quantity: 1, PickOutcome.Picked, employeeId: 1, at: At("09:00")),
            Line(quantity: 1, PickOutcome.Picked, employeeId: 1, at: At("09:30")),
            Line(quantity: 1, PickOutcome.Picked, employeeId: 1, at: At("09:15"))
        );

        var label = LabelContent.From(order, Names((1, "Jordan")));

        Assert.Equal(At("09:30"), label.PickedAt);
    }

    [Fact]
    public void IsHeld_WhenAnyLineHasAnUnresolvedIssue()
    {
        var order = OrderWith(
            Line(quantity: 1, PickOutcome.Picked, employeeId: 1, at: At("09:00")),
            Line(quantity: 1, PickOutcome.HasIssue, employeeId: 1, at: At("09:01"))
        );

        var label = LabelContent.From(order, Names((1, "Jordan")));

        Assert.True(label.IsHeld);
        Assert.Equal(["Pikachu ex"], label.UnresolvedProducts);
    }

    // Branch review round 3, BR-001. The review lets a picker finish with products they never
    // looked at (016 FR-019a), and an order was held only for a reported issue: a picker who pulled
    // one product of two got "Pick complete" and a ready-to-pack label for a short sleeve. A product
    // with no outcome is unresolved (016 FR-019), so the order is not complete.
    [Fact]
    public void IsHeld_WhenAnyLineWasNeverLookedAt()
    {
        var order = OrderWith(
            Line(quantity: 2, PickOutcome.Picked, employeeId: 1, at: At("09:00")),
            Line(
                quantity: 3,
                pickOutcome: null,
                employeeId: null,
                at: null,
                productName: "Charizard ex"
            )
        );

        var label = LabelContent.From(order, Names((1, "Jordan")));

        Assert.True(label.IsHeld);
        Assert.Equal(["Charizard ex"], label.UnresolvedProducts);
        Assert.Equal(2, label.CardCount);
    }

    [Fact]
    public void UnresolvedProducts_ListsBothReportedAndNeverLookedAt()
    {
        var order = OrderWith(
            Line(quantity: 1, PickOutcome.Picked, employeeId: 1, at: At("09:00")),
            Line(
                quantity: 1,
                PickOutcome.HasIssue,
                employeeId: 1,
                at: At("09:01"),
                productName: "Reported Card"
            ),
            Line(
                quantity: 1,
                pickOutcome: null,
                employeeId: null,
                at: null,
                productName: "Skipped Card"
            )
        );

        var label = LabelContent.From(order, Names((1, "Jordan")));

        Assert.True(label.IsHeld);
        Assert.Equal(["Reported Card", "Skipped Card"], label.UnresolvedProducts);
    }

    [Fact]
    public void IsHeld_IsFalse_WhenEveryLineIsPicked()
    {
        var order = OrderWith(
            Line(quantity: 2, PickOutcome.Picked, employeeId: 1, at: At("09:00"))
        );

        var label = LabelContent.From(order, Names((1, "Jordan")));

        Assert.False(label.IsHeld);
        Assert.Empty(label.UnresolvedProducts);
    }

    [Fact]
    public void SetAsideCount_IsUnknown_BecauseNothingRecordsOneYet()
    {
        // FR-046. Printing a zero would tell a manager "no cards set aside" on every hold label,
        // which is worse than saying nothing. The property exists so that adding the capability in
        // the issue-resolution feature does not reopen a printed label's layout.
        var order = OrderWith(
            Line(quantity: 1, PickOutcome.HasIssue, employeeId: 1, at: At("09:00"))
        );

        var label = LabelContent.From(order, Names((1, "Jordan")));

        Assert.Null(label.SetAsideCount);
    }

    [Fact]
    public void ShipsShort_IsFalse_BecauseNothingSetsItInThisFeature()
    {
        var order = OrderWith(
            Line(quantity: 1, PickOutcome.Picked, employeeId: 1, at: At("09:00"))
        );

        var label = LabelContent.From(order, Names((1, "Jordan")));

        Assert.False(label.ShipsShort);
    }

    [Fact]
    public void HasStarted_IsFalse_WhenNoOutcomeHasBeenRecorded()
    {
        // An order nobody has touched has no label to print (FR-009). The guard is "has picking
        // happened", not "is the order Picked" — a held order is never Picked and must still print.
        var order = OrderWith(Line(quantity: 1, pickOutcome: null, employeeId: null, at: null));

        var label = LabelContent.From(order, Names());

        Assert.False(label.HasStarted);
        Assert.Empty(label.PickedBy);
        Assert.Null(label.PickedAt);
    }

    [Fact]
    public void HasStarted_IsTrue_ForAHeldOrderThatIsNotPicked()
    {
        var order = OrderWith(
            Line(quantity: 1, PickOutcome.HasIssue, employeeId: 1, at: At("09:00"))
        );

        var label = LabelContent.From(order, Names((1, "Jordan")));

        Assert.True(label.HasStarted);
    }

    // ---- helpers -------------------------------------------------------------------------

    private static DateTimeOffset At(string time) => DateTimeOffset.Parse($"2026-09-21T{time}:00Z");

    private static IReadOnlyDictionary<int, string> Names(params (int Id, string Name)[] people) =>
        people.ToDictionary(person => person.Id, person => person.Name);

    private static Order OrderWith(params OrderLine[] lines) =>
        new()
        {
            Id = 121,
            TcgplayerOrderId = "F8433182-7B9B3B-BC75E",
            Status = OrderStatus.InProgress,
            ImportedAt = At("08:00"),
            OrderLines = lines,
        };

    private static OrderLine Line(
        int quantity,
        PickOutcome? pickOutcome,
        int? employeeId,
        DateTimeOffset? at,
        string productName = "Pikachu ex"
    ) =>
        new()
        {
            RawDescription = "Pikachu ex - 001/197 - Near Mint",
            ProductLine = "Pokemon",
            ProductName = productName,
            Set = "Surging Sparks",
            CollectorNumber = "001/197",
            Condition = "Near Mint",
            Quantity = quantity,
            PickOutcome = pickOutcome,
            PickOutcomeRecordedByEmployeeId = employeeId,
            PickOutcomeRecordedAt = at,
        };
}
