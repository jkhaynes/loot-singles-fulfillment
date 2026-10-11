using LootSingles.Application.Import;

namespace LootSingles.UnitTests.Import;

public class OrderLineFailureMessageTests
{
    private const string Description =
        "Pokemon - SV: Black Bolt: Genesect ex - #067/086 - Double Rare - Near Mint";

    [Theory]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("")]
    public void UnparseableQuantity_MessageCarriesOrderPrefixAndRawText(string quantity)
    {
        var result = LineOutcome.Run(
            new RawProductLine { QuantityText = quantity, RawDescription = Description }
        );

        Assert.Equal(FailureType.InvalidQuantity, result.FailureType);
        Assert.Equal(
            $"Order 'SYN-0001': Quantity '{quantity}' is not a positive whole number for product line '{Description}'.",
            result.FailureMessage
        );
    }

    [Fact]
    public void ExtractionFailure_MessageCarriesOrderPrefix()
    {
        var description = "Pokemon - SV: Black Bolt: Genesect ex - Double Rare - Near Mint";
        var result = LineOutcome.Run(
            new RawProductLine { QuantityText = "1", RawDescription = description }
        );

        Assert.Equal(FailureType.MissingCollectorNumber, result.FailureType);
        Assert.Equal(
            $"Order 'SYN-0001': Product description has no collector number: '{description}'.",
            result.FailureMessage
        );
    }
}
