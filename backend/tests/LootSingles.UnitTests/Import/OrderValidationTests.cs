using LootSingles.Application.Import;

namespace LootSingles.UnitTests.Import;

public class OrderValidationTests
{
    private const string ValidDescription =
        "Pokemon - SV: Black Bolt: Genesect ex - #067/086 - Double Rare - Near Mint";

    [Theory]
    [InlineData("", ValidDescription, FailureType.InvalidQuantity)]
    [InlineData("0", ValidDescription, FailureType.InvalidQuantity)]
    [InlineData("-1", ValidDescription, FailureType.InvalidQuantity)]
    [InlineData("abc", ValidDescription, FailureType.InvalidQuantity)]
    [InlineData("1", "Pokemon - SV: Black Bolt:  - #067/086 - Double Rare - Near Mint", FailureType.MissingProductName)]
    [InlineData("1", "Pokemon - : Genesect ex - #067/086 - Double Rare - Near Mint", FailureType.MissingSet)]
    [InlineData("1", "Pokemon - SV: Black Bolt: Genesect ex - Double Rare - Near Mint", FailureType.MissingCollectorNumber)]
    [InlineData("1", "Pokemon - SV: Black Bolt: Genesect ex - #067/086 - Double Rare - Unknown", FailureType.MissingCondition)]
    public void Validate_InvalidRequiredField_ReturnsSpecificFailure(
        string quantity,
        string description,
        FailureType expectedFailure)
    {
        var result = OrderLineValidator.Validate(new RawProductLine
        {
            QuantityText = quantity,
            RawDescription = description,
        });

        Assert.False(result.IsValid);
        Assert.Equal(expectedFailure, result.FailureType);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureMessage));
    }

    [Fact]
    public void Validate_MissingOptionalRarityAndVariant_RemainsValid()
    {
        var result = OrderLineValidator.Validate(new RawProductLine
        {
            QuantityText = "1",
            RawDescription = "Pokemon - SV: Black Bolt: Genesect ex - #067/086 - Near Mint",
        });

        Assert.True(result.IsValid);
        Assert.Null(result.OrderLine!.Rarity);
        Assert.Null(result.OrderLine.Variant);
    }
}
