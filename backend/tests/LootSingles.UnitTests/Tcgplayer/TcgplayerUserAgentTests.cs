using System.Reflection;
using LootSingles.Infrastructure.Tcgplayer;

namespace LootSingles.UnitTests.Tcgplayer;

/// <summary>
/// T023: every TCGplayer request names the business, the application and its accurate version
/// (FR-022, research.md §11). The version is the Infrastructure assembly's InformationalVersion
/// with any "+hash" suffix trimmed.
/// </summary>
public sealed class TcgplayerUserAgentTests
{
    [Fact]
    public void Value_IsApplicationNameAndAssemblyVersionWithoutHash()
    {
        var informational = typeof(TcgplayerUserAgent)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;
        var version = informational.Split('+')[0];

        Assert.Equal(
            $"LootSinglesFulfillment/{version} (Loot Investments LLC)",
            TcgplayerUserAgent.Value
        );
        Assert.DoesNotContain('+', TcgplayerUserAgent.Value);
    }

    [Theory]
    [InlineData("1.0.0+64314de0e298006fa460384dfff40043e0d9c334", "1.0.0")]
    [InlineData("1.0.0+abc1234", "1.0.0")]
    [InlineData("0.0.0-local", "0.0.0-local")]
    public void For_TrimsTheSourceLinkHashSuffix(string informationalVersion, string expected) =>
        Assert.Equal(
            $"LootSinglesFulfillment/{expected} (Loot Investments LLC)",
            TcgplayerUserAgent.For(informationalVersion)
        );
}
