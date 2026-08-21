using LootSingles.Infrastructure.Auth;

namespace LootSingles.UnitTests.Auth;

public class Pbkdf2PinHasherTests
{
    [Fact]
    public void Verify_CorrectPin_ReturnsTrue()
    {
        var hasher = new Pbkdf2PinHasher();
        var hash = hasher.Hash("1234");

        Assert.True(hasher.Verify(hash, "1234"));
    }

    [Fact]
    public void Verify_WrongPin_ReturnsFalse()
    {
        var hasher = new Pbkdf2PinHasher();
        var hash = hasher.Hash("1234");

        Assert.False(hasher.Verify(hash, "4321"));
    }

    [Fact]
    public void Hash_NeverEqualsTheRawPin()
    {
        var hasher = new Pbkdf2PinHasher();

        Assert.NotEqual("1234", hasher.Hash("1234"));
    }
}
