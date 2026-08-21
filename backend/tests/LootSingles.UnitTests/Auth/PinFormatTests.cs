using LootSingles.Application.Auth;

namespace LootSingles.UnitTests.Auth;

public class PinFormatTests
{
    [Theory]
    [InlineData("1234")]
    [InlineData("0000")]
    [InlineData("9999")]
    public void IsValid_FourNumericDigits_ReturnsTrue(string pin)
    {
        Assert.True(PinFormat.IsValid(pin));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    [InlineData(" 1234")]
    public void IsValid_NotExactlyFourNumericDigits_ReturnsFalse(string? pin)
    {
        Assert.False(PinFormat.IsValid(pin));
    }
}
