using LootSingles.Application.Packing;

namespace LootSingles.UnitTests.Packing;

/// <summary>
/// 017-pick-completion-handoff T033 — what arrives in the packing desk's box.
///
/// <para>
/// A scanner is a keyboard: it types exactly what is encoded and the desk cannot know which
/// device fired. Three shapes arrive — a link from the label's QR, a bare order number a person
/// typed, and the full TCGplayer identifier from the Code 128 — so they are normalised once here
/// rather than three times down the lookup (research.md §10).
/// </para>
/// </summary>
public sealed class PackingCodeResolverTests
{
    [Theory]
    [InlineData("121")]
    [InlineData("  121  ")]
    [InlineData("121\r")] // a scanner's carriage-return terminator
    [InlineData("https://loot.example/packing/121")]
    [InlineData("https://loot.example/packing/121?from=scan")]
    [InlineData("https://loot.example/orders/121")]
    [InlineData("/packing/121")]
    public void Resolves_AnOrderNumber(string input)
    {
        var code = PackingCodeResolver.Resolve(input);

        Assert.Equal(121, code.OrderId);
        Assert.Null(code.TcgplayerOrderId);
    }

    [Theory]
    [InlineData("F8433182-7B9B3B-BC75E")]
    [InlineData("  f8433182-7b9b3b-bc75e  ")]
    public void Resolves_ATcgplayerIdentifier(string input)
    {
        var code = PackingCodeResolver.Resolve(input);

        Assert.Null(code.OrderId);
        // Case is normalised so a scanner and a person reach the same order.
        Assert.Equal("F8433182-7B9B3B-BC75E", code.TcgplayerOrderId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://loot.example/packing/")]
    [InlineData("https://loot.example/")]
    public void Resolves_NothingFromAnEmptyOrShapelessInput(string input)
    {
        var code = PackingCodeResolver.Resolve(input);

        Assert.Null(code.OrderId);
        Assert.Null(code.TcgplayerOrderId);
        Assert.False(code.IsResolvable);
    }

    [Fact]
    public void ANumberTooLargeForAnOrderId_IsTreatedAsAnIdentifier()
    {
        // Never guesses. An input that cannot be an order number is passed on as an identifier
        // for the lookup to fail honestly, rather than being coerced into a wrong order.
        var code = PackingCodeResolver.Resolve("99999999999999");

        Assert.Null(code.OrderId);
        Assert.Equal("99999999999999", code.TcgplayerOrderId);
    }
}
