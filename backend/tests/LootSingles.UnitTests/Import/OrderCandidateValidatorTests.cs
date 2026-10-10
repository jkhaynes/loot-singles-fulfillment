using LootSingles.Application.Import;

namespace LootSingles.UnitTests.Import;

public class OrderCandidateValidatorTests
{
    private const string Raw = "Pikachu - 025/165 - 151 (Near Mint)";

    private static OrderLineCandidate Line(
        string rawDescription = Raw,
        string? productName = "Pikachu",
        string? set = "151",
        string? collectorNumber = "025/165",
        string? condition = "Near Mint",
        int? quantity = 1
    ) =>
        new(
            rawDescription,
            ProductLine: "Pokemon",
            productName,
            set,
            collectorNumber,
            Rarity: "Common",
            condition,
            Variant: null,
            Language: "English",
            ImageUrl: null,
            quantity
        );

    private static OrderCandidate Order(string? identifier, params OrderLineCandidate[] lines) =>
        new(identifier, lines);

    [Fact]
    public void Validate_ValidCandidate_ReturnsNull()
    {
        Assert.Null(OrderCandidateValidator.Validate(Order("SYN-0001", Line())));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingOrderIdentifier_Rejects(string? identifier)
    {
        var result = OrderCandidateValidator.Validate(Order(identifier, Line()));

        Assert.Equal(FailureType.MissingOrderIdentifier, result!.Value.Type);
        Assert.Equal("An order page is missing its order identifier.", result.Value.Message);
    }

    [Fact]
    public void Validate_NoLines_Rejects()
    {
        var result = OrderCandidateValidator.Validate(Order("SYN-0001"));

        Assert.Equal(FailureType.NoProductLines, result!.Value.Type);
        Assert.Equal("Order 'SYN-0001' contains no product lines.", result.Value.Message);
    }

    [Fact]
    public void Validate_IdentifierCheckRunsBeforeLineCheck()
    {
        var result = OrderCandidateValidator.Validate(Order(null));

        Assert.Equal(FailureType.MissingOrderIdentifier, result!.Value.Type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidQuantity_Rejects(int? quantity)
    {
        var result = OrderCandidateValidator.Validate(Order("SYN-0001", Line(quantity: quantity)));

        Assert.Equal(FailureType.InvalidQuantity, result!.Value.Type);
        Assert.Equal(
            $"Order 'SYN-0001': Quantity '{quantity}' is not a positive whole number for product line '{Raw}'.",
            result.Value.Message
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_MissingProductName_Rejects(string? name)
    {
        var result = OrderCandidateValidator.Validate(Order("SYN-0001", Line(productName: name)));

        Assert.Equal(FailureType.MissingProductName, result!.Value.Type);
        Assert.Equal(
            $"Order 'SYN-0001': Product name is missing for product line '{Raw}'.",
            result.Value.Message
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_MissingSet_Rejects(string? set)
    {
        var result = OrderCandidateValidator.Validate(Order("SYN-0001", Line(set: set)));

        Assert.Equal(FailureType.MissingSet, result!.Value.Type);
        Assert.Equal(
            $"Order 'SYN-0001': Set is missing for product line '{Raw}'.",
            result.Value.Message
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_MissingCondition_Rejects(string? condition)
    {
        var result = OrderCandidateValidator.Validate(
            Order("SYN-0001", Line(condition: condition))
        );

        Assert.Equal(FailureType.MissingCondition, result!.Value.Type);
        Assert.Equal(
            $"Order 'SYN-0001': Condition is missing or unrecognized for product line '{Raw}'.",
            result.Value.Message
        );
    }

    [Fact]
    public void Validate_LineRulesRunInOrder_QuantityBeforeNameBeforeSetBeforeCondition()
    {
        var line = Line(productName: null, set: null, condition: null, quantity: 0);
        Assert.Equal(
            FailureType.InvalidQuantity,
            OrderCandidateValidator.Validate(Order("SYN-0001", line))!.Value.Type
        );

        line = line with { Quantity = 1 };
        Assert.Equal(
            FailureType.MissingProductName,
            OrderCandidateValidator.Validate(Order("SYN-0001", line))!.Value.Type
        );

        line = line with { ProductName = "Pikachu" };
        Assert.Equal(
            FailureType.MissingSet,
            OrderCandidateValidator.Validate(Order("SYN-0001", line))!.Value.Type
        );

        line = line with { Set = "151" };
        Assert.Equal(
            FailureType.MissingCondition,
            OrderCandidateValidator.Validate(Order("SYN-0001", line))!.Value.Type
        );
    }

    [Fact]
    public void Validate_FirstFailingLineWins()
    {
        var result = OrderCandidateValidator.Validate(
            Order("SYN-0001", Line(), Line(rawDescription: "Second", set: null), Line(quantity: 0))
        );

        Assert.Equal(FailureType.MissingSet, result!.Value.Type);
        Assert.Contains("'Second'", result.Value.Message);
    }

    [Fact]
    public void Validate_NullCollectorNumber_IsValid()
    {
        Assert.Null(
            OrderCandidateValidator.Validate(Order("SYN-0001", Line(collectorNumber: null)))
        );
    }

    [Fact]
    public void Validate_RejectedBySource_ReturnsItAsIsWithoutRunningRules()
    {
        var candidate = new OrderCandidate(
            null,
            [],
            (FailureType.MissingCollectorNumber, "adapter says no")
        );

        var result = OrderCandidateValidator.Validate(candidate);

        Assert.Equal(FailureType.MissingCollectorNumber, result!.Value.Type);
        Assert.Equal("adapter says no", result.Value.Message);
    }
}
